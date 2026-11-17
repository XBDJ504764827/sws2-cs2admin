using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.Translation;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Models;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Applies bans, checks joins and records audit entries.
/// </summary>
internal sealed class BanService
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly BanStore _store;
    private readonly AdminService _admins;
    private readonly AuditLogger _audit;
    private AdminSettings _settings;
    private volatile bool _stopped;
    private readonly CancellationTokenSource _shutdown = new();

    public void Stop()
    {
        _stopped = true;
        _shutdown.Cancel();
        _store.DrainWrites();
    }

    public BanService(
        ISwiftlyCore core,
        BanStore store,
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

    public void ApplySettings(AdminSettings settings)
    {
        _settings = settings;
    }

    public BanStore Store => _store;

    public bool KickIfBanned(IPlayer player)
    {
        if (!_settings.Bans.CheckOnJoin
            || player is null
            || player.IsFakeClient
            || !(player.Controller?.IsValid ?? false))
        {
            return false;
        }

        var ban = _store.FindActive(player.SteamID);
        if (ban is null)
        {
            return false;
        }

        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var expiry = FormatDuration(ban.ExpiresAt is null ? null : ban.ExpiresAt - DateTimeOffset.UtcNow, localizer);
        var reason = string.Format(
            _settings.Bans.KickMessage,
            string.IsNullOrWhiteSpace(ban.Reason) ? localizer["cs2admin.ban.no_reason"] : ban.Reason,
            expiry);

        _audit.Log(
            AdminActor.Console,
            "ban.enforce",
            ban.SteamId,
            ban.PlayerName,
            $"Join blocked; expiry={expiry}");

        _logger.LogInformation(
            "[sws2-cs2admin] Blocked banned player {PlayerName} ({SteamId})",
            player.Name,
            player.SteamID);

        player.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
        return true;
    }

    public Task<bool> BanAsync(AdminActor actor, IPlayer target, TimeSpan? duration, string reason)
    {
        if (!CheckPermission(actor, AdminPermissions.Ban, out var localizer))
        {
            return Task.FromResult(false);
        }

        if (!_admins.CanTarget(actor, target))
        {
            Reply(actor, localizer["cs2admin.action.immunity", target.Name]);
            _audit.Log(actor, "ban.denied", target.SteamID, target.Name, "Target immunity", success: false);
            return Task.FromResult(false);
        }

        if (actor.SteamId != 0 && actor.SteamId == target.SteamID)
        {
            Reply(actor, localizer["cs2admin.action.self"]);
            return Task.FromResult(false);
        }

        var existing = _store.FindActive(target.SteamID);
        if (existing != null)
        {
            Reply(actor, localizer["cs2admin.ban.already", target.Name, FormatDuration(existing.ExpiresAt is null ? null : existing.ExpiresAt - DateTimeOffset.UtcNow, localizer)]);
            return Task.FromResult(false);
        }

        duration = ClampDuration(duration);
        var record = new BanRecord
        {
            SteamId = target.SteamID,
            PlayerName = target.Name,
            IpAddress = target.IPAddress,
            Reason = string.IsNullOrWhiteSpace(reason) ? localizer["cs2admin.ban.no_reason"] : reason.Trim(),
            AdminSteamId = actor.SteamId,
            AdminName = actor.Name,
            ExpiresAt = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null
        };

        return PersistBanAsync(actor, record);
    }

    public Task<bool> AddBanAsync(AdminActor actor, ulong steamId, string playerName, TimeSpan? duration, string reason)
    {
        if (!CheckPermission(actor, AdminPermissions.Ban, out var localizer))
        {
            return Task.FromResult(false);
        }

        if (steamId == 0)
        {
            Reply(actor, localizer["cs2admin.target.invalid"]);
            return Task.FromResult(false);
        }

        var existing = _store.FindActive(steamId);
        if (existing != null)
        {
            Reply(actor, localizer["cs2admin.ban.already", playerName, FormatDuration(existing.ExpiresAt is null ? null : existing.ExpiresAt - DateTimeOffset.UtcNow, localizer)]);
            return Task.FromResult(false);
        }

        var target = _core.PlayerManager.GetPlayerFromSteamId(steamId);
        if (target != null && !_admins.CanTarget(actor, target))
        {
            Reply(actor, localizer["cs2admin.action.immunity", target.Name]);
            _audit.Log(actor, "ban.denied", target.SteamID, target.Name, "Target immunity", success: false);
            return Task.FromResult(false);
        }

        duration = ClampDuration(duration);
        var record = new BanRecord
        {
            SteamId = steamId,
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? steamId.ToString() : playerName.Trim(),
            IpAddress = target?.IPAddress,
            Reason = string.IsNullOrWhiteSpace(reason) ? localizer["cs2admin.ban.no_reason"] : reason.Trim(),
            AdminSteamId = actor.SteamId,
            AdminName = actor.Name,
            ExpiresAt = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null
        };

        return PersistBanAsync(actor, record);
    }

    private async Task<bool> PersistBanAsync(AdminActor actor, BanRecord record)
    {
        try
        {
            var added = await _store.AddAsync(record).ConfigureAwait(false);
            return await OnGameThread(() =>
            {
                var localizer = LocalizerFor(actor);
                if (!added)
                {
                    Reply(actor, localizer["cs2admin.ban.already", record.PlayerName, "-"]);
                    return false;
                }
                _audit.Log(actor, "ban.add", record.SteamId, record.PlayerName,
                    $"duration={FormatDuration(record.ExpiresAt is null ? null : record.ExpiresAt - DateTimeOffset.UtcNow, localizer)}; reason={record.Reason}");
                Reply(actor, localizer["cs2admin.ban.success", record.PlayerName, record.SteamId,
                    FormatDuration(record.ExpiresAt is null ? null : record.ExpiresAt - DateTimeOffset.UtcNow, localizer), record.Reason]);
                AnnounceBan(actor, record, localizer);
                var online = _core.PlayerManager.GetPlayerFromSteamId(record.SteamId);
                if (online is { IsValid: true } && _store.FindActive(record.SteamId)?.Id == record.Id) KickBanned(online, record, localizer);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to persist ban; no success was reported");
            return await OnGameThread(() => { Reply(actor, LocalizerFor(actor)["cs2admin.ban.storage_failed"]); return false; }).ConfigureAwait(false);
        }
    }

    public async Task<bool> UnbanAsync(AdminActor actor, ulong steamId, string reason, Guid? expectedId = null)
    {
        // Called on the game thread before yielding to the database worker.
        if (!_settings.Bans.AllowUnban)
        {
            Reply(actor, _core.Localizer["cs2admin.unban.disabled"]);
            return false;
        }
        if (!CheckPermission(actor, AdminPermissions.Unban, out var localizer)) return false;
        if (string.IsNullOrWhiteSpace(reason)) reason = localizer["cs2admin.ban.no_reason"];
        try
        {
            var record = await _store.RevokeAsync(steamId, actor.Name, reason, expectedId).ConfigureAwait(false);
            return await OnGameThread(() =>
            {
                if (record is null) { Reply(actor, localizer["cs2admin.unban.none", steamId]); return false; }
                _audit.Log(actor, "ban.remove", steamId, record.PlayerName, reason);
                Reply(actor, localizer["cs2admin.unban.success", record.PlayerName, steamId]);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to persist unban");
            return await OnGameThread(() => { Reply(actor, localizer["cs2admin.ban.storage_failed"]); return false; }).ConfigureAwait(false);
        }
    }

    private async Task<bool> OnGameThread(Func<bool> action)
    {
        if (_stopped) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _shutdown.Token.Register(() => completion.TrySetResult(false));
        _core.Scheduler.NextTick(() =>
        {
            try { completion.TrySetResult(!_stopped && action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private void KickBanned(IPlayer target, BanRecord record, ILocalizer adminLocalizer)
    {
        var targetLocalizer = _core.Translation.GetPlayerLocalizer(target);
        var expiry = FormatDuration(record.ExpiresAt is null ? null : record.ExpiresAt - DateTimeOffset.UtcNow, targetLocalizer);
        var reason = string.Format(
            _settings.Bans.KickMessage,
            string.IsNullOrWhiteSpace(record.Reason) ? targetLocalizer["cs2admin.ban.no_reason"] : record.Reason,
            expiry);

        target.Kick(reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
    }

    private void AnnounceBan(AdminActor actor, BanRecord record, ILocalizer localizer)
    {
        if (_settings.Notify.BroadcastBans)
        {
            _core.PlayerManager.SendChat($"{localizer["cs2admin.prefix"]} {localizer["cs2admin.ban.broadcast", record.PlayerName, record.Reason]}");
            return;
        }

        if (_settings.Notify.OnlineAdmins)
        {
            NotifyAdmins($"{localizer["cs2admin.prefix"]} {localizer["cs2admin.ban.broadcast", record.PlayerName, record.Reason]}", actor);
        }
    }

    private void NotifyAdmins(string message, AdminActor actor)
    {
        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            if (player.SteamID == actor.SteamId || !_admins.IsAdmin(player.SteamID))
            {
                continue;
            }

            player.SendChat(message);
        }
    }

    private TimeSpan? ClampDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            // The command parser already supplies the configured default. Explicit null means permanent.
            return null;
        }

        if (_settings.Bans.MaxDurationMinutes > 0
            && duration.Value.TotalMinutes > _settings.Bans.MaxDurationMinutes)
        {
            return TimeSpan.FromMinutes(_settings.Bans.MaxDurationMinutes);
        }

        return duration;
    }

    private static string FormatDuration(TimeSpan? duration, ILocalizer localizer)
    {
        return duration is null ? localizer["cs2admin.duration.permanent"] : DurationParser.Format(duration);
    }

    private bool CheckPermission(AdminActor actor, string node, out ILocalizer localizer)
    {
        localizer = LocalizerFor(actor);

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
        return actor.Player is { IsValid: true } player && player.SteamID == actor.SteamId
            ? _core.Translation.GetPlayerLocalizer(player) : _core.Localizer;
    }

    private void Reply(AdminActor actor, string message)
    {
        if (actor.Player is { IsValid: true } && actor.Player.SteamID == actor.SteamId)
        {
            actor.Player.SendChat($"{_core.Translation.GetPlayerLocalizer(actor.Player)["cs2admin.prefix"]} {message}");
        }
        else
        {
            _logger.LogInformation("[sws2-cs2admin] {Message}", message);
        }
    }
}
