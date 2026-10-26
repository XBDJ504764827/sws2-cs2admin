using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Player actions with permission, immunity, feedback and audit handling.
/// </summary>
internal sealed class AdminActionService
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly AdminService _admins;
    private readonly AuditLogger _audit;
    private readonly CommService _comms;
    private readonly BanService _bans;
    private readonly ConcurrentDictionary<int, int> _freezeTokens = new();

    private AdminSettings _settings;

    public AdminActionService(
        ISwiftlyCore core,
        AdminService admins,
        AuditLogger audit,
        CommService comms,
        BanService bans,
        AdminSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _admins = admins;
        _audit = audit;
        _comms = comms;
        _bans = bans;
        _settings = settings;
    }

    public void ApplySettings(AdminSettings settings)
    {
        _settings = settings;
    }

    public bool Kick(AdminActor actor, IPlayer target, string reason)
    {
        if (!CanAct(actor, target, AdminPermissions.Kick, out var localizer))
        {
            return false;
        }

        var text = string.IsNullOrWhiteSpace(reason) ? localizer["cs2admin.action.kick.default"] : reason.Trim();
        target.Kick(text, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);

        _audit.Log(actor, "player.kick", target.SteamID, target.Name, text);
        Announce(actor, localizer["cs2admin.action.kick.success", target.Name, text], localizer);
        return true;
    }

    public bool Slay(AdminActor actor, IPlayer target)
    {
        if (!CanAct(actor, target, AdminPermissions.Slay, out var localizer))
        {
            return false;
        }

        if (!target.IsAlive)
        {
            Reply(actor, localizer["cs2admin.action.target_dead", target.Name]);
            return false;
        }

        target.TakeDamage(9999f, DamageTypes_t.DMG_GENERIC);
        _audit.Log(actor, "player.slay", target.SteamID, target.Name);
        Announce(actor, localizer["cs2admin.action.slay.success", target.Name], localizer);
        return true;
    }

    public bool Slap(AdminActor actor, IPlayer target, int damage)
    {
        if (!CanAct(actor, target, AdminPermissions.Slap, out var localizer))
        {
            return false;
        }

        if (!target.IsAlive)
        {
            Reply(actor, localizer["cs2admin.action.target_dead", target.Name]);
            return false;
        }

        damage = Math.Clamp(damage, 1, 1000);
        target.TakeDamage(damage, DamageTypes_t.DMG_GENERIC);
        _audit.Log(actor, "player.slap", target.SteamID, target.Name, $"damage={damage}");
        Announce(actor, localizer["cs2admin.action.slap.success", target.Name, damage], localizer);
        return true;
    }

    public bool Respawn(AdminActor actor, IPlayer target)
    {
        if (!CanAct(actor, target, AdminPermissions.Respawn, out var localizer))
        {
            return false;
        }

        target.Respawn();
        _audit.Log(actor, "player.respawn", target.SteamID, target.Name);
        Announce(actor, localizer["cs2admin.action.respawn.success", target.Name], localizer);
        return true;
    }

    public bool Freeze(AdminActor actor, IPlayer target, int seconds)
    {
        if (!CanAct(actor, target, AdminPermissions.Freeze, out var localizer))
        {
            return false;
        }

        var pawn = target.PlayerPawn;
        if (pawn is not { IsValid: true })
        {
            Reply(actor, localizer["cs2admin.action.target_invalid", target.Name]);
            return false;
        }

        seconds = Math.Clamp(seconds, 1, 300);
        var token = _freezeTokens.AddOrUpdate(target.PlayerID, 1, (_, current) => current + 1);
        var playerId = target.PlayerID;

        pawn.MoveType = MoveType_t.MOVETYPE_NONE;
        pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
        pawn.MoveTypeUpdated();

        _core.Scheduler.DelayBySeconds(seconds, () =>
        {
            if (!_freezeTokens.TryGetValue(playerId, out var current) || current != token)
            {
                return;
            }

            _freezeTokens.TryRemove(playerId, out _);
            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player?.PlayerPawn is not { IsValid: true } currentPawn)
            {
                return;
            }

            currentPawn.MoveType = MoveType_t.MOVETYPE_WALK;
            currentPawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
            currentPawn.MoveTypeUpdated();

            var targetLocalizer = _core.Translation.GetPlayerLocalizer(player);
            player.SendChat($"{targetLocalizer["cs2admin.prefix"]} {targetLocalizer["cs2admin.action.freeze.over"]}");
        });

        _audit.Log(actor, "player.freeze", target.SteamID, target.Name, $"seconds={seconds}");
        Announce(actor, localizer["cs2admin.action.freeze.success", target.Name, seconds], localizer);
        return true;
    }

    public bool Mute(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
        => _comms.Mute(actor, target, duration, reason);

    public bool Gag(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
        => _comms.Gag(actor, target, duration, reason);

    public bool Silence(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
        => _comms.Silence(actor, target, duration, reason);

    public bool Unmute(AdminActor actor, IPlayer target, string reason)
        => _comms.Unmute(actor, target, reason);

    public bool Ungag(AdminActor actor, IPlayer target, string reason)
        => _comms.Ungag(actor, target, reason);

    public bool Unsilence(AdminActor actor, IPlayer target, string reason)
        => _comms.Unsilence(actor, target, reason);

    public bool Ban(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
        => _bans.Ban(actor, target, duration, reason);

    public bool AddBan(AdminActor actor, ulong steamId, string playerName, TimeSpan? duration, string reason)
        => _bans.AddBan(actor, steamId, playerName, duration, reason);

    public bool Unban(AdminActor actor, ulong steamId, string reason)
        => _bans.Unban(actor, steamId, reason);

    public void CleanupPlayer(int playerId)
    {
        _freezeTokens.TryRemove(playerId, out _);
    }

    public void Reset()
    {
        _freezeTokens.Clear();
    }

    private bool CanAct(AdminActor actor, IPlayer target, string permission, out ILocalizer localizer)
    {
        localizer = LocalizerFor(actor);

        if (!actor.IsConsole && !_admins.HasPermission(actor.SteamId, permission))
        {
            Reply(actor, localizer["cs2admin.no_permission"]);
            _audit.Log(actor, "permission.denied", details: permission, success: false);
            return false;
        }

        if (!_admins.CanTarget(actor, target))
        {
            Reply(actor, localizer["cs2admin.action.immunity", target.Name]);
            _audit.Log(actor, "action.denied", target.SteamID, target.Name, "Target immunity", success: false);
            return false;
        }

        if (actor.SteamId != 0 && actor.SteamId == target.SteamID)
        {
            Reply(actor, localizer["cs2admin.action.self"]);
            return false;
        }

        if (target.Controller is not { IsValid: true })
        {
            Reply(actor, localizer["cs2admin.action.target_invalid", target.Name]);
            return false;
        }

        return true;
    }

    private void Announce(AdminActor actor, string message, ILocalizer localizer)
    {
        Reply(actor, message);

        if (!_settings.Notify.OnlineAdmins)
        {
            return;
        }

        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            if (player.SteamID == actor.SteamId || !_admins.IsAdmin(player.SteamID))
            {
                continue;
            }

            player.SendChat($"{localizer["cs2admin.prefix"]} {message}");
        }
    }

    private ILocalizer LocalizerFor(AdminActor actor)
    {
        return actor.Player != null ? _core.Translation.GetPlayerLocalizer(actor.Player) : _core.Localizer;
    }

    private void Reply(AdminActor actor, string message)
    {
        if (actor.Player != null)
        {
            actor.Player.SendChat($"{_core.Translation.GetPlayerLocalizer(actor.Player)["cs2admin.prefix"]} {message}");
        }
        else
        {
            _logger.LogInformation("[sws2-cs2admin] {Message}", message);
        }
    }
}
