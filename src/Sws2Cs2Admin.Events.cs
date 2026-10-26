using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;

namespace Sws2Cs2Admin;

/// <summary>
/// Join checks, comm enforcement and cleanup.
/// </summary>
public partial class Sws2Cs2Admin
{
    private void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent @event)
    {
        Core.Scheduler.NextTick(() => CheckJoin(@event.PlayerId));
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent @event)
    {
        Core.Scheduler.NextTick(() => CheckJoin(@event.PlayerId));
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        _actions.CleanupPlayer(@event.PlayerId);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _actions.Reset();
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        var player = @event.UserIdPlayer;
        if (player != null && !_bans.KickIfBanned(player))
        {
            _comms.ApplyPlayer(player);
        }

        return HookResult.Continue;
    }

    private void CheckJoin(int playerId)
    {
        if (!_loaded)
        {
            return;
        }

        var player = Core.PlayerManager.GetPlayer(playerId);
        if (player is null || player.IsFakeClient || !(player.Controller?.IsValid ?? false))
        {
            return;
        }

        if (_bans.KickIfBanned(player))
        {
            return;
        }

        _comms.ApplyPlayer(player);
    }
}
