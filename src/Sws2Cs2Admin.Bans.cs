using System.Globalization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Models;
using Sws2Cs2Admin.Services;
using Sws2Cs2Menus.Contracts;

namespace Sws2Cs2Admin;

public partial class Sws2Cs2Admin
{
    private readonly Dictionary<int, Guid> _banRequests = [];

    private bool IsCurrentActor(AdminActor actor) => actor.IsConsole ||
        actor.Player is { IsValid: true } player && player.SteamID == actor.SteamId;

    [Command("bans", false, "", "Search bans: [name/SteamID] [--status=active|permanent|temporary|expired|revoked|all] [--admin=name] [--from=YYYY-MM-DD] [--to=YYYY-MM-DD]")]
    public void BansCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);
        if (!CheckPermission(context, actor, AdminPermissions.BansView)) return;
        if (!TryParseBanQuery(context.Args, out var query))
        {
            Reply(context, localizer, "cs2admin.bans.usage");
            return;
        }
        if (actor.Player is null) { _ = ListBanHistoryAsync(actor, query); return; }
        if (_menus is not { IsAvailable: true }) { Reply(context, localizer, "cs2admin.menu.unavailable"); return; }
        _menus.Open(actor.Player, BuildBanFilters(actor.Player, query));
        RequestBanPage(actor.Player, query, 0);
    }

    private static bool TryParseBanQuery(string[] args, out BanQuery query)
    {
        query = new BanQuery();
        var names = new List<string>();
        foreach (var argument in args)
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal)) { names.Add(argument); continue; }
            var parts = argument[2..].Split('=', 2);
            if (parts.Length != 2) return false;
            switch (parts[0].ToLowerInvariant())
            {
                case "status":
                    if (!Enum.TryParse<BanStatus>(parts[1], true, out var status) || !Enum.IsDefined(status)) return false;
                    query = query with { Status = status };
                    break;
                case "admin": query = query with { Admin = parts[1] }; break;
                case "from":
                case "to":
                    if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date)) return false;
                    var utc = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
                    if (parts[0].Equals("from", StringComparison.OrdinalIgnoreCase)) query = query with { From = utc };
                    else
                    {
                        if (date == DateTime.MaxValue.Date) return false;
                        query = query with { To = utc.AddDays(1) };
                    }
                    break;
                default: return false;
            }
        }
        var text = string.Join(' ', names).Trim();
        query = TargetResolver.TryParseSteamId(text, out var id)
            ? query with { SteamId = id, Text = text } : query with { Text = text };
        return !query.From.HasValue || !query.To.HasValue || query.From < query.To;
    }

    private IMenuDefinition BuildBanFilters(IPlayer player, BanQuery? query = null)
    {
        query ??= new BanQuery();
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.bans"])
            .Id($"admin.banfilters.{player.PlayerID}")

            .Comment(localizer["cs2admin.bans.search_hint"])
            .PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
        foreach (var status in Enum.GetValues<BanStatus>())
        {
            var selected = query with { Status = status };
            builder.AddOption(option => option.Id($"status-{status}")
                .Text(localizer[$"cs2admin.bans.status.{status.ToString().ToLowerInvariant()}"])
                .OnSelected(context => RequestBanPage(context.Player, selected, 0)));
        }
        return builder.Build();
    }

    private void RequestBanPage(IPlayer player, BanQuery query, int page, bool replacePage = false)
    {
        if (_menus is not { IsAvailable: true } || !_admins.HasPermission(player, AdminPermissions.BansView)) return;
        var request = Guid.NewGuid();
        _banRequests[player.PlayerID] = request;
        var expected = _menus.GetCurrentMenu(player);
        var actor = AdminActor.FromPlayer(player);
        _ = LoadBanPageAsync(actor, query, page, replacePage, expected, request);
    }

    private async Task LoadBanPageAsync(AdminActor actor, BanQuery query, int page, bool replacePage,
        IMenuDefinition? expected, Guid request)
    {
        try
        {
            var result = await _bans.Store.QueryAsync(query, page, 3).ConfigureAwait(false);
            Core.Scheduler.NextTick(() =>
            {
                if (!_loaded || !IsCurrentActor(actor) || _menus is not { IsAvailable: true }
                    || !_banRequests.TryGetValue(actor.Player!.PlayerID, out var current) || current != request
                    || !_admins.HasPermission(actor.Player, AdminPermissions.BansView)
                    || !ReferenceEquals(_menus.GetCurrentMenu(actor.Player), expected)) return;
                var menu = BuildBanResults(actor.Player, query, result);
                if (replacePage && !_menus.Back(actor.Player)) return;
                _menus.OpenSubMenu(actor.Player, menu);
            });
        }
        catch (Exception exception)
        {
            Core.Logger.LogError(exception, "[sws2-cs2admin] Ban search failed");
            Core.Scheduler.NextTick(() =>
            {
                if (_loaded && IsCurrentActor(actor))
                    ReplyMapChange(actor.Player!, Core.Translation.GetPlayerLocalizer(actor.Player!), "cs2admin.ban.storage_failed");
            });
        }
    }

    private IMenuDefinition BuildBanResults(IPlayer player, BanQuery query, BanPage page)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        var status = localizer[$"cs2admin.bans.status.{query.Status.ToString().ToLowerInvariant()}"];
        var pages = Math.Max(1, (page.Total + page.PageSize - 1) / page.PageSize);
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.bans"])
            .Id($"admin.banresults.{Guid.NewGuid():N}")

            .Comment(localizer["cs2admin.bans.results", status, page.Total, page.Page + 1, pages, query.Text])
            .PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
        foreach (var ban in page.Records)
            builder.AddOption(option => option.Id($"ban-{ban.Id}").Text($"{ban.PlayerName} · {ban.SteamId}")
                .Comment($"{ban.Reason} · {ban.AdminName}")
                .OnSelected(context => { if (_admins.HasPermission(context.Player, AdminPermissions.BansView)) context.Service.OpenSubMenu(context.Player, BuildBanDetails(context.Player, ban)); }));
        if (page.Total == 0) builder.AddOption(option => option.Id("empty").Text(localizer["cs2admin.menu.no_bans"]).Disabled());
        if (page.Page > 0) builder.AddOption(option => option.Id("previous-results").Text(localizer["cs2admin.bans.previous"])
            .OnSelected(context => RequestBanPage(context.Player, query, page.Page - 1, true)));
        if (page.Page + 1 < pages) builder.AddOption(option => option.Id("next-results").Text(localizer["cs2admin.bans.next"])
            .OnSelected(context => RequestBanPage(context.Player, query, page.Page + 1, true)));
        return builder.Build();
    }

    private IMenuDefinition BuildBanDetails(IPlayer player, BanRecord ban)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        var builder = _menus!.CreateMenu(ban.PlayerName).Id($"admin.bandetail.{ban.Id}")
            .Comment($"SteamID: {ban.SteamId}")
            .PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
        if (ban.IsActive(DateTimeOffset.UtcNow))
            builder.AddOption(option => option.Id("unban").Text(localizer["cs2admin.bans.unban"])
                .Disabled(!_settings.Bans.AllowUnban || !_admins.HasPermission(player, AdminPermissions.Unban))
                .OnSelected(context => { if (_admins.HasPermission(context.Player, AdminPermissions.Unban)) context.Service.OpenSubMenu(context.Player, BuildUnbanReasons(context.Player, ban)); }));
        void Field(string key, string value) => builder.AddOption(option => option.Id(key)
            .Text(localizer[$"cs2admin.bans.field.{key}"]).Comment(value).Disabled());
        Field("reason", ban.Reason);
        Field("admin", $"{ban.AdminName} · {ban.AdminSteamId}");
        Field("created", ban.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        Field("expires", ban.ExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? localizer["cs2admin.duration.permanent"]);
        if (ban.RevokedAt.HasValue)
        {
            Field("revoked", $"{ban.RevokedBy} · {ban.RevokedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Field("revoke_reason", ban.RevokeReason ?? "-");
        }
        return builder.Build();
    }

    private IMenuDefinition BuildUnbanReasons(IPlayer player, BanRecord ban)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        var builder = _menus!.CreateMenu(localizer["cs2admin.bans.unban"])
            .Id($"admin.unbanreason.{ban.Id}")
            .Comment(localizer["cs2admin.bans.reason_hint"])
            .PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
        foreach (var key in new[] { "mistake", "appeal", "review" })
        {
            var reason = localizer[$"cs2admin.bans.reason.{key}"];
            builder.AddOption(option => option.Id(key).Text(reason)
                .OnSelected(context => { if (_admins.HasPermission(context.Player, AdminPermissions.Unban)) context.Service.OpenSubMenu(context.Player, BuildUnbanConfirm(context.Player, ban, reason)); }));
        }
        return builder.Build();
    }

    private IMenuDefinition BuildUnbanConfirm(IPlayer player, BanRecord ban, string reason)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        var builder = _menus!.CreateMenu(localizer["cs2admin.bans.unban"])
            .Id($"admin.unbanconfirm.{ban.Id}").Layout(MenuLayout.Confirmation)

            .Comment(localizer["cs2admin.bans.confirm", ban.PlayerName, ban.SteamId, reason])
            .PageSize(2).ShowBack().ShowClose().CloseOnSelect();
        builder.AddOption(option => option.Id("confirm").Text(localizer["cs2admin.bans.confirm_action"])
            .OnSelected(context => { _ = _bans.UnbanAsync(AdminActor.FromPlayer(context.Player), ban.SteamId, reason, ban.Id); }));
        builder.AddOption(option => option.Id("cancel").Text(localizer["cs2admin.menu.action.ban.cancel"])
            .OnSelected(context => context.Service.Back(context.Player)));
        return builder.Build();
    }
}
