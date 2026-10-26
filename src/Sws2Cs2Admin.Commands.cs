using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Models;
using Sws2Cs2Admin.Services;
using Sws2Cs2Menus.Contracts;

namespace Sws2Cs2Admin;

/// <summary>
/// Admin chat/console commands. Console commands use the raw names; players use
/// the same names with the configured chat prefix.
/// </summary>
public partial class Sws2Cs2Admin
{
    [Command("admin", false, "", "Open the admin menu")]
    public void AdminMenuCommand(ICommandContext context)
    {
        if (!context.IsSentByPlayer || context.Sender is null)
        {
            Reply(context, Core.Localizer, "cs2admin.only_players");
            return;
        }

        if (!_admins.IsAdmin(context.Sender.SteamID))
        {
            Reply(context, Core.Translation.GetPlayerLocalizer(context.Sender), "cs2admin.no_permission");
            return;
        }

        if (_menus is not { IsAvailable: true })
        {
            Reply(context, Core.Translation.GetPlayerLocalizer(context.Sender), "cs2admin.menu.unavailable");
            return;
        }

        var result = _menus.Open(context.Sender, BuildAdminMenu(context.Sender));
        if (result != MenuOpenResult.Opened)
        {
            Reply(context, Core.Translation.GetPlayerLocalizer(context.Sender), "cs2admin.menu.failed", result.ToString());
        }
    }

    [Command("kick", false, "", "Kick a player")]
    public void KickCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Kick))
        {
            return;
        }

        var targets = ResolveTargets(context, actor, localizer);
        if (targets.Count == 0)
        {
            return;
        }

        var reason = JoinArgs(context.Args, 1);
        foreach (var target in targets)
        {
            _actions.Kick(actor, target, reason);
        }
    }

    [Command("slay", false, "", "Slay a player")]
    public void SlayCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Slay))
        {
            return;
        }

        foreach (var target in ResolveTargets(context, actor, localizer))
        {
            _actions.Slay(actor, target);
        }
    }

    [Command("slap", false, "", "Damage a player")]
    public void SlapCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Slap))
        {
            return;
        }

        var damage = context.Args.Length > 1 && int.TryParse(context.Args[1], out var parsed) ? parsed : 10;
        foreach (var target in ResolveTargets(context, actor, localizer))
        {
            _actions.Slap(actor, target, damage);
        }
    }

    [Command("respawn", false, "", "Respawn a player")]
    public void RespawnCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Respawn))
        {
            return;
        }

        foreach (var target in ResolveTargets(context, actor, localizer))
        {
            _actions.Respawn(actor, target);
        }
    }

    [Command("freeze", false, "", "Freeze a player")]
    public void FreezeCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Freeze))
        {
            return;
        }

        var seconds = context.Args.Length > 1 && int.TryParse(context.Args[1], out var parsed) ? parsed : 5;
        foreach (var target in ResolveTargets(context, actor, localizer))
        {
            _actions.Freeze(actor, target, seconds);
        }
    }

    [Command("mute", false, "", "Mute voice chat")]
    public void MuteCommand(ICommandContext context)
    {
        CommCommand(context, AdminPermissions.Mute, CommType.Mute, _actions.Mute);
    }

    [Command("gag", false, "", "Gag text chat")]
    public void GagCommand(ICommandContext context)
    {
        CommCommand(context, AdminPermissions.Gag, CommType.Gag, _actions.Gag);
    }

    [Command("silence", false, "", "Mute and gag a player")]
    public void SilenceCommand(ICommandContext context)
    {
        CommCommand(context, AdminPermissions.Silence, CommType.Silence, _actions.Silence);
    }

    [Command("unmute", false, "", "Remove a voice mute")]
    public void UnmuteCommand(ICommandContext context)
    {
        RevokeCommCommand(context, AdminPermissions.Mute, _actions.Unmute);
    }

    [Command("ungag", false, "", "Remove a text gag")]
    public void UngagCommand(ICommandContext context)
    {
        RevokeCommCommand(context, AdminPermissions.Gag, _actions.Ungag);
    }

    [Command("unsilence", false, "", "Remove a silence")]
    public void UnsilenceCommand(ICommandContext context)
    {
        RevokeCommCommand(context, AdminPermissions.Silence, _actions.Unsilence);
    }

    [Command("ban", false, "", "Ban a player")]
    public void BanCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Ban))
        {
            return;
        }

        var targets = ResolveTargets(context, actor, localizer);
        if (targets.Count == 0)
        {
            return;
        }

        ParseDurationAndReason(context.Args, 1, _settings.Bans.DefaultDurationMinutes, out var duration, out var reason);
        foreach (var target in targets)
        {
            _actions.Ban(actor, target, duration, reason);
        }
    }

    [Command("addban", false, "", "Ban a SteamID")]
    public void AddBanCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Ban))
        {
            return;
        }

        if (context.Args.Length == 0 || !TargetResolver.TryParseSteamId(context.Args[0], out var steamId))
        {
            Reply(context, localizer, "cs2admin.usage.addban");
            return;
        }

        ParseDurationAndReason(context.Args, 1, _settings.Bans.DefaultDurationMinutes, out var duration, out var reason);
        var online = Core.PlayerManager.GetPlayerFromSteamId(steamId);
        _actions.AddBan(actor, steamId, online?.Name ?? steamId.ToString(), duration, reason);
    }

    [Command("unban", false, "", "Lift a ban")]
    public void UnbanCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Unban))
        {
            return;
        }

        if (context.Args.Length == 0 || !TargetResolver.TryParseSteamId(context.Args[0], out var steamId))
        {
            Reply(context, localizer, "cs2admin.usage.unban");
            return;
        }

        _actions.Unban(actor, steamId, JoinArgs(context.Args, 1));
    }

    [Command("listbans", false, "", "List recent bans")]
    public void ListBansCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.BansView))
        {
            return;
        }

        ulong? steamId = null;
        if (context.Args.Length > 0)
        {
            if (TargetResolver.TryParseSteamId(context.Args[0], out var parsed))
            {
                steamId = parsed;
            }
            else
            {
                var target = TargetResolver.ResolveSingle(Core, context.Sender, context.Args[0]);
                if (target == null)
                {
                    Reply(context, localizer, "cs2admin.target.invalid");
                    return;
                }

                steamId = target.SteamID;
            }
        }

        var records = _bans.Store.History(steamId, 10);
        if (records.Count == 0)
        {
            Reply(context, localizer, "cs2admin.ban.list.none");
            return;
        }

        Reply(context, localizer, "cs2admin.ban.list.header");
        foreach (var record in records)
        {
            Reply(
                context,
                localizer,
                "cs2admin.ban.list.entry",
                record.PlayerName,
                record.SteamId,
                record.Reason,
                localizer[record.IsActive(DateTimeOffset.UtcNow) ? "cs2admin.ban.active" : "cs2admin.ban.inactive"],
                record.ExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? localizer["cs2admin.duration.permanent"]);
        }
    }

    [Command("who", false, "", "List online admins")]
    public void WhoCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Who))
        {
            return;
        }

        if (context.Args.Length > 0)
        {
            var target = TargetResolver.ResolveSingle(Core, context.Sender, context.Args[0]);
            if (target == null)
            {
                Reply(context, localizer, "cs2admin.target.invalid");
                return;
            }

            var info = _admins.GetAdmin(target.SteamID);
            Reply(
                context,
                localizer,
                "cs2admin.who.target",
                target.Name,
                target.SteamID,
                info == null ? "-" : string.Join(", ", info.Groups),
                info?.Immunity ?? 0,
                info == null ? "-" : string.Join(", ", info.Permissions));
            return;
        }

        var admins = Core.PlayerManager.GetAllValidPlayers()
            .Where(player => _admins.IsAdmin(player.SteamID))
            .OrderBy(player => player.Name)
            .ToArray();

        if (admins.Length == 0)
        {
            Reply(context, localizer, "cs2admin.who.none");
            return;
        }

        Reply(context, localizer, "cs2admin.who.header");
        foreach (var player in admins)
        {
            var info = _admins.GetAdmin(player.SteamID);
            Reply(
                context,
                localizer,
                "cs2admin.who.entry",
                player.Name,
                player.SteamID,
                info?.Immunity ?? 0,
                info == null ? "-" : string.Join(", ", info.Groups));
        }
    }

    [Command("reloadadmins", false, "", "Reload the admin roster")]
    public void ReloadAdminsCommand(ICommandContext context)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, AdminPermissions.Reload))
        {
            return;
        }

        var success = _admins.Reload();
        _audit.Log(actor, "admin.reload", details: success ? "success" : "failed", success: success);
        Reply(context, localizer, success ? "cs2admin.reload.success" : "cs2admin.reload.failed");
    }

    [Command("adminhelp", false, "", "Show admin commands")]
    public void AdminHelpCommand(ICommandContext context)
    {
        var localizer = Localizer(context);
        Reply(context, localizer, "cs2admin.help.header");

        foreach (var command in new[]
        {
            "admin", "kick", "slay", "slap", "respawn", "freeze",
            "mute", "unmute", "gag", "ungag", "silence", "unsilence",
            "ban", "addban", "unban", "listbans", "who", "reloadadmins"
        })
        {
            Reply(context, localizer, "cs2admin.help.entry", command);
        }
    }

    private void CommCommand(
        ICommandContext context,
        string permission,
        CommType type,
        Func<AdminActor, IPlayer, TimeSpan?, string, bool> action)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, permission))
        {
            return;
        }

        var targets = ResolveTargets(context, actor, localizer);
        if (targets.Count == 0)
        {
            return;
        }

        ParseDurationAndReason(context.Args, 1, _settings.Comms.DefaultDurationMinutes, out var duration, out var reason);
        foreach (var target in targets)
        {
            action(actor, target, duration, reason);
        }
    }

    private void RevokeCommCommand(
        ICommandContext context,
        string permission,
        Func<AdminActor, IPlayer, string, bool> action)
    {
        var actor = Actor(context);
        var localizer = Localizer(context);

        if (!CheckPermission(context, actor, permission))
        {
            return;
        }

        foreach (var target in ResolveTargets(context, actor, localizer))
        {
            action(actor, target, JoinArgs(context.Args, 1));
        }
    }

    private AdminActor Actor(ICommandContext context)
    {
        return context is { IsSentByPlayer: true, Sender: not null }
            ? AdminActor.FromPlayer(context.Sender)
            : AdminActor.Console;
    }

    private ILocalizer Localizer(ICommandContext context)
    {
        return context.Sender != null
            ? Core.Translation.GetPlayerLocalizer(context.Sender)
            : Core.Localizer;
    }

    private bool CheckPermission(ICommandContext context, AdminActor actor, string node)
    {
        if (actor.IsConsole || _admins.HasPermission(actor.SteamId, node))
        {
            return true;
        }

        _audit.Log(actor, "permission.denied", details: node, success: false);
        Reply(context, Localizer(context), "cs2admin.no_permission");
        return false;
    }

    private IReadOnlyList<IPlayer> ResolveTargets(ICommandContext context, AdminActor actor, ILocalizer localizer)
    {
        if (context.Args.Length == 0)
        {
            Reply(context, localizer, "cs2admin.target.missing");
            return [];
        }

        var targets = TargetResolver.ResolveOnline(Core, context.Sender, context.Args[0]);
        if (targets.Count == 0)
        {
            Reply(context, localizer, "cs2admin.target.invalid");
        }

        return targets;
    }

    private static void ParseDurationAndReason(
        string[] args,
        int startIndex,
        int defaultMinutes,
        out TimeSpan? duration,
        out string reason)
    {
        var index = startIndex;

        if (args.Length > index && DurationParser.TryParse(args[index], out var parsed))
        {
            duration = parsed;
            index++;
        }
        else
        {
            duration = defaultMinutes > 0 ? TimeSpan.FromMinutes(defaultMinutes) : null;
        }

        reason = JoinArgs(args, index);
    }

    private static string JoinArgs(string[] args, int startIndex)
    {
        return args.Length <= startIndex ? string.Empty : string.Join(' ', args[startIndex..]).Trim();
    }

    private void Reply(ICommandContext context, ILocalizer localizer, string key, params object[] args)
    {
        var text = args.Length == 0 ? localizer[key] : localizer[key, args];
        context.Reply($"{localizer["cs2admin.prefix"]} {text}");
    }
}
