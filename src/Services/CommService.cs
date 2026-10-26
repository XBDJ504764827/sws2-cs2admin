using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Models;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Applies mute/gag/silence blocks and enforces text-chat gags.
/// </summary>
internal sealed class CommService : IDisposable
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly CommStore _store;
    private readonly AdminService _admins;
    private readonly AuditLogger _audit;
    private readonly object _sync = new();

    private AdminSettings _settings;
    private IDisposable? _expiryTimer;
    private Guid _chatHook;
    private bool _disposed;

    public CommService(
        ISwiftlyCore core,
        CommStore store,
        AdminService admins,
        AuditLogger audit,
        AdminSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _store = store;
        _admins = admins;
        _audit = audit;
        _settings = settings;
    }

    public void Start()
    {
        _chatHook = _core.Command.HookClientChat(OnClientChat);
        _expiryTimer = _core.Scheduler.RepeatBySeconds(
            Math.Clamp(_settings.Comms.ExpireCheckSeconds, 5, 3600),
            ExpireBlocks);
    }

    public void ApplySettings(AdminSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
        }
    }

    public void ApplyPlayer(IPlayer player)
    {
        if (!player.IsValid || player.IsFakeClient)
        {
            return;
        }

        if (_store.IsMuted(player.SteamID))
        {
            player.VoiceFlags |= VoiceFlagValue.Muted;
        }
    }

    public bool Mute(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
    {
        return ApplyBlock(actor, target, CommType.Mute, AdminPermissions.Mute, duration, reason);
    }

    public bool Gag(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
    {
        return ApplyBlock(actor, target, CommType.Gag, AdminPermissions.Gag, duration, reason);
    }

    public bool Silence(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
    {
        return ApplyBlock(actor, target, CommType.Silence, AdminPermissions.Silence, duration, reason);
    }

    public bool Unmute(AdminActor actor, IPlayer target, string reason)
    {
        return RevokeBlock(actor, target, CommType.Mute, AdminPermissions.Mute, reason);
    }

    public bool Ungag(AdminActor actor, IPlayer target, string reason)
    {
        return RevokeBlock(actor, target, CommType.Gag, AdminPermissions.Gag, reason);
    }

    public bool Unsilence(AdminActor actor, IPlayer target, string reason)
    {
        return RevokeBlock(actor, target, CommType.Silence, AdminPermissions.Silence, reason);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _expiryTimer?.Dispose();
        _expiryTimer = null;

        if (_chatHook != Guid.Empty)
        {
            _core.Command.UnhookClientChat(_chatHook);
            _chatHook = Guid.Empty;
        }
    }

    private bool ApplyBlock(
        AdminActor actor,
        IPlayer target,
        CommType type,
        string permission,
        TimeSpan? duration,
        string reason)
    {
        var localizer = LocalizerFor(actor);

        if (!CheckPermission(actor, permission, localizer))
        {
            return false;
        }

        if (!_admins.CanTarget(actor, target))
        {
            Reply(actor, localizer["cs2admin.action.immunity", target.Name]);
            _audit.Log(actor, $"{type}.denied", target.SteamID, target.Name, "Target immunity", success: false);
            return false;
        }

        if (actor.SteamId != 0 && actor.SteamId == target.SteamID)
        {
            Reply(actor, localizer["cs2admin.action.self"]);
            return false;
        }

        duration = ClampDuration(duration);
        var record = new CommRecord
        {
            SteamId = target.SteamID,
            PlayerName = target.Name,
            Type = type,
            Reason = string.IsNullOrWhiteSpace(reason) ? localizer["cs2admin.comm.no_reason"] : reason.Trim(),
            AdminSteamId = actor.SteamId,
            AdminName = actor.Name,
            ExpiresAt = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null
        };

        _store.Add(record);
        ApplyPlayer(target);
        _audit.Log(
            actor,
            $"{type}.add",
            target.SteamID,
            target.Name,
            $"duration={FormatDuration(duration, localizer)}; reason={record.Reason}");

        var targetLocalizer = _core.Translation.GetPlayerLocalizer(target);
        target.SendChat($"{targetLocalizer["cs2admin.prefix"]} {targetLocalizer[$"cs2admin.comm.{type.ToString().ToLowerInvariant()}.target", FormatDuration(duration, targetLocalizer), record.Reason]}");
        Reply(actor, localizer[$"cs2admin.comm.{type.ToString().ToLowerInvariant()}.success", target.Name, FormatDuration(duration, localizer), record.Reason]);
        return true;
    }

    private bool RevokeBlock(
        AdminActor actor,
        IPlayer target,
        CommType type,
        string permission,
        string reason)
    {
        var localizer = LocalizerFor(actor);

        if (!CheckPermission(actor, permission, localizer))
        {
            return false;
        }

        if (!_admins.CanTarget(actor, target))
        {
            Reply(actor, localizer["cs2admin.action.immunity", target.Name]);
            return false;
        }

        if (!_store.Revoke(target.SteamID, type, actor.Name, reason))
        {
            Reply(actor, localizer[$"cs2admin.comm.{type.ToString().ToLowerInvariant()}.none", target.Name]);
            return false;
        }

        if (!_store.IsMuted(target.SteamID))
        {
            target.VoiceFlags &= ~VoiceFlagValue.Muted;
        }

        _audit.Log(actor, $"{type}.remove", target.SteamID, target.Name, reason);
        Reply(actor, localizer[$"cs2admin.comm.{type.ToString().ToLowerInvariant()}.removed", target.Name]);

        var targetLocalizer = _core.Translation.GetPlayerLocalizer(target);
        target.SendChat($"{targetLocalizer["cs2admin.prefix"]} {targetLocalizer[$"cs2admin.comm.{type.ToString().ToLowerInvariant()}.removed_target"]}");
        return true;
    }

    private HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        if (!_settings.Comms.EnforceGag)
        {
            return HookResult.Continue;
        }

        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player is not { IsValid: true } || !_store.IsGagged(player.SteamID))
        {
            return HookResult.Continue;
        }

        var localizer = _core.Translation.GetPlayerLocalizer(player);
        player.SendChat($"{localizer["cs2admin.prefix"]} {localizer["cs2admin.comm.gagged"]}");
        return HookResult.Stop;
    }

    private void ExpireBlocks()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _store.Expired(now);

        foreach (var record in expired)
        {
            _store.Revoke(record.SteamId, record.Type, "system", "Expired");

            var player = _core.PlayerManager.GetPlayerFromSteamId(record.SteamId);
            if (player is not { IsValid: true })
            {
                continue;
            }

            if (!_store.IsMuted(player.SteamID))
            {
                player.VoiceFlags &= ~VoiceFlagValue.Muted;
            }

            var localizer = _core.Translation.GetPlayerLocalizer(player);
            player.SendChat($"{localizer["cs2admin.prefix"]} {localizer["cs2admin.comm.expired", record.Type.ToString().ToLowerInvariant()]}");
        }
    }

    private TimeSpan? ClampDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return _settings.Comms.DefaultDurationMinutes > 0
                ? TimeSpan.FromMinutes(_settings.Comms.DefaultDurationMinutes)
                : null;
        }

        if (_settings.Comms.MaxDurationMinutes > 0
            && duration.Value.TotalMinutes > _settings.Comms.MaxDurationMinutes)
        {
            return TimeSpan.FromMinutes(_settings.Comms.MaxDurationMinutes);
        }

        return duration;
    }

    private bool CheckPermission(AdminActor actor, string node, ILocalizer localizer)
    {
        if (actor.IsConsole || _admins.HasPermission(actor.SteamId, node))
        {
            return true;
        }

        Reply(actor, localizer["cs2admin.no_permission"]);
        _audit.Log(actor, "permission.denied", details: node, success: false);
        return false;
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

    private static string FormatDuration(TimeSpan? duration, ILocalizer localizer)
    {
        return duration is null ? localizer["cs2admin.duration.permanent"] : DurationParser.Format(duration);
    }
}
