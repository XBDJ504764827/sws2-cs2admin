using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Models;
using Sws2Cs2Admin.Services;
using Sws2Cs2MapManager.Contracts;
using Sws2Cs2Menus.Contracts;

namespace Sws2Cs2Admin;

/// <summary>
/// Admin panel built with the shared sws2-cs2menus service.
/// </summary>
public partial class Sws2Cs2Admin
{
    private IMenuDefinition BuildAdminMenu(IPlayer admin)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(admin);
        var actor = AdminActor.FromPlayer(admin);

        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.title"])
            .Id("admin.root")
            .Comment(localizer["cs2admin.menu.comment"])
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        if (HasAnyPermission(admin, AdminPermissions.Kick, AdminPermissions.Slay, AdminPermissions.Slap,
                AdminPermissions.Respawn, AdminPermissions.Freeze, AdminPermissions.Mute, AdminPermissions.Gag,
                AdminPermissions.Silence, AdminPermissions.Ban))
        {
            builder.AddOption(option => option
                .Id("players")
                .Text(localizer["cs2admin.menu.players"])
                .Comment(localizer["cs2admin.menu.players.comment"])
                .SubMenu(BuildPlayersMenu(admin, actor, localizer)));
        }

        builder.AddOption(option => option
            .Id("maps")
            .Text(localizer["cs2admin.menu.maps"])
            .Comment(localizer["cs2admin.menu.maps.comment"])
            .SubMenu(BuildMapsMenu(admin, localizer)));

        builder.AddOption(option => option
            .Id("bans")
            .Text(localizer["cs2admin.menu.bans"])
            .Comment(localizer["cs2admin.menu.bans.comment"])
            .Permission(AdminPermissions.BansView)
            .SubMenu(BuildBansMenu(admin, actor, localizer)));

        builder.AddOption(option => option
            .Id("server")
            .Text(localizer["cs2admin.menu.server"])
            .Comment(localizer["cs2admin.menu.server.comment"])
            .Permission(AdminPermissions.Reload)
            .SubMenu(BuildServerMenu(admin, actor, localizer)));

        builder.AddOption(option => option
            .Id("myinfo")
            .Text(localizer["cs2admin.menu.myinfo"])
            .SubMenu(BuildMyInfoMenu(admin, localizer)));

        builder.AddOption(option => option
            .Id("close")
            .Text(localizer["cs2admin.menu.close"])
            .OnSelected(context => context.Service.Close(context.Player)));

        return builder.Build();
    }

    private IMenuDefinition BuildPlayersMenu(IPlayer admin, AdminActor actor, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.players"])
            .Id($"admin.players.{admin.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        var players = Core.PlayerManager.GetAllValidPlayers()
            .Where(player => !player.IsFakeClient)
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (players.Length == 0)
        {
            builder.AddOption(option => option
                .Id("empty")
                .Text(localizer["cs2admin.menu.no_players"])
                .Disabled());
            return builder.Build();
        }

        foreach (var target in players)
        {
            var canTarget = _admins.CanTarget(actor, target);
            builder.AddOption(option => option
                .Id($"target-{target.PlayerID}")
                .Text($"{target.Name} [{TargetResolver.TeamName(target)}]")
                .Comment($"#{target.PlayerID} | {target.SteamID}")
                .Disabled(!canTarget)
                .SubMenu(canTarget ? BuildPlayerActionMenu(admin, actor, target, localizer) : null));
        }

        return builder.Build();
    }

    private IMenuDefinition BuildPlayerActionMenu(
        IPlayer admin,
        AdminActor actor,
        IPlayer target,
        ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu($"{localizer["cs2admin.menu.actions"]}: {target.Name}")
            .Id($"admin.actions.{admin.PlayerID}.{target.PlayerID}")
            .Comment($"#{target.PlayerID} | {target.SteamID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        builder.AddOption(option => option
            .Id("kick")
            .Text(localizer["cs2admin.menu.action.kick"])
            .Permission(AdminPermissions.Kick)
            .OnSelected(context => context.Service.OpenSubMenu(
                context.Player,
                BuildKickConfirmMenu(admin, actor, target, localizer))));

        builder.AddOption(option => option
            .Id("slay")
            .Text(localizer["cs2admin.menu.action.slay"])
            .Permission(AdminPermissions.Slay)
            .OnSelected(context => _actions.Slay(actor, target)));

        builder.AddOption(option => option
            .Id("slap")
            .Text(localizer["cs2admin.menu.action.slap"])
            .Permission(AdminPermissions.Slap)
            .OnSelected(context => _actions.Slap(actor, target, 10)));

        builder.AddOption(option => option
            .Id("respawn")
            .Text(localizer["cs2admin.menu.action.respawn"])
            .Permission(AdminPermissions.Respawn)
            .OnSelected(context => _actions.Respawn(actor, target)));

        builder.AddOption(option => option
            .Id("freeze")
            .Text(localizer["cs2admin.menu.action.freeze"])
            .Permission(AdminPermissions.Freeze)
            .OnSelected(context => _actions.Freeze(actor, target, 5)));

        var commDuration = _settings.Comms.DefaultDurationMinutes > 0
            ? TimeSpan.FromMinutes(_settings.Comms.DefaultDurationMinutes)
            : (TimeSpan?)null;

        builder.AddOption(option => option
            .Id("mute")
            .Text(localizer["cs2admin.menu.action.mute"])
            .Permission(AdminPermissions.Mute)
            .OnSelected(context => _actions.Mute(actor, target, commDuration, localizer["cs2admin.menu.reason.comm"])));

        builder.AddOption(option => option
            .Id("gag")
            .Text(localizer["cs2admin.menu.action.gag"])
            .Permission(AdminPermissions.Gag)
            .OnSelected(context => _actions.Gag(actor, target, commDuration, localizer["cs2admin.menu.reason.comm"])));

        builder.AddOption(option => option
            .Id("silence")
            .Text(localizer["cs2admin.menu.action.silence"])
            .Permission(AdminPermissions.Silence)
            .OnSelected(context => _actions.Silence(actor, target, commDuration, localizer["cs2admin.menu.reason.comm"])));

        builder.AddOption(option => option
            .Id("ban")
            .Text(localizer["cs2admin.menu.action.ban"])
            .Permission(AdminPermissions.Ban)
            .OnSelected(context => context.Service.OpenSubMenu(
                context.Player,
                BuildBanDurationMenu(admin, actor, target, localizer))));

        return builder.Build();
    }

    private IMenuDefinition BuildKickConfirmMenu(IPlayer admin, AdminActor actor, IPlayer target, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.action.kick.confirm", target.Name])
            .Id($"admin.kick.confirm.{admin.PlayerID}.{target.PlayerID}")
            .Comment($"{target.Name} | #{target.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect();

        builder.AddOption(option => option
            .Id("confirm")
            .Text(localizer["cs2admin.menu.action.kick.confirm"])
            .Permission(AdminPermissions.Kick)
            .OnSelected(context => _actions.Kick(actor, target, localizer["cs2admin.menu.reason.kick"])));

        builder.AddOption(option => option
            .Id("cancel")
            .Text(localizer["cs2admin.menu.action.kick.cancel"])
            .OnSelected(context => context.Service.Back(context.Player)));

        return builder.Build();
    }

    private IMenuDefinition BuildBanDurationMenu(
        IPlayer admin,
        AdminActor actor,
        IPlayer target,
        ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu($"{localizer["cs2admin.menu.action.ban"]}: {target.Name}")
            .Id($"admin.ban.{admin.PlayerID}.{target.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        AddBanDuration(builder, admin, actor, target, localizer, "30s", TimeSpan.FromSeconds(30));
        AddBanDuration(builder, admin, actor, target, localizer, "60s", TimeSpan.FromSeconds(60));
        AddBanDuration(builder, admin, actor, target, localizer, "1h", TimeSpan.FromHours(1));
        AddBanDuration(builder, admin, actor, target, localizer, "1d", TimeSpan.FromDays(1));
        AddBanDuration(builder, admin, actor, target, localizer, "3d", TimeSpan.FromDays(3));
        AddBanDuration(builder, admin, actor, target, localizer, "7d", TimeSpan.FromDays(7));
        AddBanDuration(builder, admin, actor, target, localizer, "30d", TimeSpan.FromDays(30));
        AddBanDuration(builder, admin, actor, target, localizer, "perm", null);

        return builder.Build();
    }

    private void AddBanDuration(
        IMenuBuilder builder,
        IPlayer admin,
        AdminActor actor,
        IPlayer target,
        ILocalizer localizer,
        string id,
        TimeSpan? duration)
    {
        builder.AddOption(option => option
            .Id($"duration-{id}")
            .Text(localizer[$"cs2admin.menu.duration.{id}"])
            .Permission(AdminPermissions.Ban)
            .OnSelected(context => context.Service.OpenSubMenu(
                context.Player,
                BuildBanConfirmMenu(admin, actor, target, localizer, duration))));
    }

    private IMenuDefinition BuildBanConfirmMenu(
        IPlayer admin,
        AdminActor actor,
        IPlayer target,
        ILocalizer localizer,
        TimeSpan? duration)
    {
        var durationText = FormatMenuBanDuration(duration, localizer);
        var builder = _menus!.CreateMenu(localizer["cs2admin.action.ban.confirm", target.Name])
            .Id($"admin.ban.confirm.{admin.PlayerID}.{target.PlayerID}.{duration?.TotalMinutes ?? 0}")
            .Comment($"{target.Name} | {durationText}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect();

        builder.AddOption(option => option
            .Id("confirm")
            .Text(localizer["cs2admin.menu.action.ban.confirm"])
            .Permission(AdminPermissions.Ban)
            .OnSelected(context => _actions.Ban(actor, target, duration, localizer["cs2admin.menu.reason.kick"])));

        builder.AddOption(option => option
            .Id("cancel")
            .Text(localizer["cs2admin.menu.action.ban.cancel"])
            .OnSelected(context => context.Service.Back(context.Player)));

        return builder.Build();
    }

    private IMenuDefinition BuildMapsMenu(IPlayer admin, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.maps"])
            .Id($"admin.maps.{admin.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        if (_mapManager is null)
        {
            builder.AddOption(option => option
                .Id("unavailable")
                .Text(localizer["cs2admin.menu.mapmanager.unavailable"])
                .Disabled());
            return builder.Build();
        }

        var maps = _mapManager.GetMaps();
        if (maps.Count == 0)
        {
            builder.AddOption(option => option
                .Id("empty")
                .Text(localizer["cs2admin.menu.no_maps"])
                .Disabled());
            return builder.Build();
        }

        foreach (var map in maps)
        {
            builder.AddOption(option => option
                .Id($"map-{map.Id}")
                .Text(map.Name)
                .Comment(map.Kind == MapKind.Workshop ? $"Workshop ID: {map.Value}" : map.Value)
                .OnSelected(context => context.Service.OpenSubMenu(
                    context.Player,
                    BuildMapConfirmMenu(admin, map, localizer))));
        }

        return builder.Build();
    }

    private IMenuDefinition BuildMapConfirmMenu(IPlayer admin, MapInfo map, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.action.map.confirm", map.Name])
            .Id($"admin.map.confirm.{admin.PlayerID}.{map.Id.Replace(':', '-')}")
            .Comment(map.Kind == MapKind.Workshop ? $"Workshop ID: {map.Value}" : map.Value)
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect();

        builder.AddOption(option => option
            .Id("confirm")
            .Text(localizer["cs2admin.menu.action.map.confirm"])
            .OnSelected(context => ChangeMapWithCountdown(admin, map, localizer)));

        builder.AddOption(option => option
            .Id("cancel")
            .Text(localizer["cs2admin.menu.action.map.cancel"])
            .OnSelected(context => context.Service.Back(context.Player)));

        return builder.Build();
    }

    private void ChangeMapWithCountdown(IPlayer admin, MapInfo map, ILocalizer localizer)
    {
        BroadcastMapCountdown(admin, map, 3, localizer);
    }

    private void BroadcastMapCountdown(IPlayer admin, MapInfo map, int secondsRemaining, ILocalizer localizer)
    {
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            var playerLocalizer = Core.Translation.GetPlayerLocalizer(player);
            player.SendChat($"{playerLocalizer["cs2admin.prefix"]} {playerLocalizer["cs2admin.action.map.countdown", map.Name, secondsRemaining]}");
        }

        if (secondsRemaining <= 1)
        {
            Core.Scheduler.DelayBySeconds(1, () => RequestManagedMapChange(admin, map, localizer));
            return;
        }

        Core.Scheduler.DelayBySeconds(1, () => BroadcastMapCountdown(admin, map, secondsRemaining - 1, localizer));
    }

    private void RequestManagedMapChange(IPlayer admin, MapInfo map, ILocalizer adminLocalizer)
    {
        if (_mapManager is null)
        {
            ReplyMapChange(admin, adminLocalizer, "cs2admin.menu.mapmanager.unavailable");
            return;
        }

        var result = _mapManager.RequestChange(map.Id);
        switch (result.Status)
        {
            case MapChangeRequestStatus.ChangeStarted:
            case MapChangeRequestStatus.DownloadStarted:
                break;
            case MapChangeRequestStatus.DownloadAlreadyPending:
                ReplyMapChange(admin, adminLocalizer, "cs2admin.action.map.download_pending", map.Name);
                break;
            case MapChangeRequestStatus.Busy:
                ReplyMapChange(admin, adminLocalizer, "cs2admin.action.map.busy");
                break;
            case MapChangeRequestStatus.MapNotFound:
            case MapChangeRequestStatus.InvalidMap:
            case MapChangeRequestStatus.WorkshopDownloadDisabled:
            case MapChangeRequestStatus.Failed:
                ReplyMapChange(admin, adminLocalizer, "cs2admin.action.map.failed", result.Error ?? map.Name);
                break;
            default:
                ReplyMapChange(admin, adminLocalizer, "cs2admin.action.map.failed", map.Name);
                break;
        }
    }

    private void OnMapChangeProgressed(object? sender, MapChangeProgressEventArgs args)
    {
        var messageKey = args.Progress switch
        {
            MapChangeProgress.DownloadStarted => "cs2admin.action.map.download_started",
            MapChangeProgress.ChangeIssued => "cs2admin.action.map.change_started",
            MapChangeProgress.Failed => "cs2admin.action.map.failed",
            _ => null
        };

        if (messageKey is not null)
        {
            BroadcastMapMessage(messageKey, args.Progress == MapChangeProgress.Failed
                ? args.Error ?? args.Map.Name
                : args.Map.Name);
        }
    }

    private void BroadcastMapMessage(string key, string mapName)
    {
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            var localizer = Core.Translation.GetPlayerLocalizer(player);
            player.SendChat($"{localizer["cs2admin.prefix"]} {localizer[key, mapName]}");
        }
    }

    private void ReplyMapChange(IPlayer admin, ILocalizer localizer, string key, params object[] args)
    {
        if (!admin.IsValid)
        {
            return;
        }

        var message = args.Length == 0 ? localizer[key] : localizer[key, args];
        admin.SendChat($"{localizer["cs2admin.prefix"]} {message}");
    }

    private IMenuDefinition BuildBansMenu(IPlayer admin, AdminActor actor, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.bans"])
            .Id($"admin.bans.{admin.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        var bans = _bans.Store.Active();
        if (bans.Count == 0)
        {
            builder.AddOption(option => option
                .Id("empty")
                .Text(localizer["cs2admin.menu.no_bans"])
                .Disabled());
            return builder.Build();
        }

        var canUnban = _admins.HasPermission(admin.SteamID, AdminPermissions.Unban);
        foreach (var ban in bans.Take(20))
        {
            builder.AddOption(option => option
                .Id($"ban-{ban.Id}")
                .Text($"{ban.PlayerName} ({ban.SteamId})")
                .Comment($"{ban.Reason} | {FormatBanExpiry(ban, localizer)}")
                .Permission(AdminPermissions.BansView)
                .Disabled(!canUnban)
                .OnSelected(context => _actions.Unban(actor, ban.SteamId, localizer["cs2admin.menu.reason.unban"])));
        }

        return builder.Build();
    }

    private IMenuDefinition BuildServerMenu(IPlayer admin, AdminActor actor, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.server"])
            .Id($"admin.server.{admin.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect();

        builder.AddOption(option => option
            .Id("reload")
            .Text(localizer["cs2admin.menu.reload"])
            .Permission(AdminPermissions.Reload)
            .OnSelected(context =>
            {
                var success = _admins.Reload();
                context.Player.SendChat($"{localizer["cs2admin.prefix"]} {localizer[success ? "cs2admin.reload.success" : "cs2admin.reload.failed"]}");
            }));

        builder.AddOption(option => option
            .Id("audit")
            .Text(localizer["cs2admin.menu.audit"])
            .Comment(_audit.GetLogDirectory())
            .Permission(AdminPermissions.Audit)
            .Disabled());

        return builder.Build();
    }

    private IMenuDefinition BuildMyInfoMenu(IPlayer admin, ILocalizer localizer)
    {
        var builder = _menus!.CreateMenu(localizer["cs2admin.menu.myinfo"])
            .Id($"admin.myinfo.{admin.PlayerID}")
            .PageSize(_settings.Menu.PageSize)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false);

        var info = _admins.GetAdmin(admin.SteamID);

        builder.AddOption(option => option
            .Id("steamid")
            .Text($"{localizer["cs2admin.menu.info.steamid"]}: {admin.SteamID}")
            .Disabled());

        builder.AddOption(option => option
            .Id("immunity")
            .Text($"{localizer["cs2admin.menu.info.immunity"]}: {info?.Immunity ?? 0}")
            .Disabled());

        builder.AddOption(option => option
            .Id("groups")
            .Text($"{localizer["cs2admin.menu.info.groups"]}: {(info == null ? "-" : string.Join(", ", info.Groups))}")
            .Disabled());

        builder.AddOption(option => option
            .Id("permissions")
            .Text($"{localizer["cs2admin.menu.info.permissions"]}: {_admins.GetPermissions(admin.SteamID).Count}")
            .Comment(string.Join(", ", _admins.GetPermissions(admin.SteamID).OrderBy(value => value)))
            .Disabled());

        return builder.Build();
    }

    private bool HasAnyPermission(IPlayer player, params string[] nodes)
    {
        return nodes.Any(node => _admins.HasPermission(player.SteamID, node));
    }

    private string FormatMenuBanDuration(TimeSpan? duration, ILocalizer localizer)
    {
        if (duration is null)
        {
            return localizer["cs2admin.menu.duration.perm"];
        }

        var durationKey = duration.Value.TotalSeconds switch
        {
            30 => "30s",
            60 => "60s",
            3600 => "1h",
            86400 => "1d",
            259200 => "3d",
            604800 => "7d",
            2592000 => "30d",
            _ => null
        };

        return durationKey is null
            ? DurationParser.Format(duration)
            : localizer[$"cs2admin.menu.duration.{durationKey}"];
    }

    private string FormatBanExpiry(BanRecord ban, ILocalizer localizer)
    {
        return ban.ExpiresAt is null
            ? localizer["cs2admin.duration.permanent"]
            : DurationParser.Format(ban.ExpiresAt - DateTimeOffset.UtcNow);
    }
}
