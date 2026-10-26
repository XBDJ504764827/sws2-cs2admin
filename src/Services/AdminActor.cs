using SwiftlyS2.Shared.Players;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// The player or console performing an admin action.
/// </summary>
internal sealed record AdminActor(ulong SteamId, string Name, bool IsConsole, IPlayer? Player)
{
    public static AdminActor Console { get; } = new(0, "Console", true, null);

    public static AdminActor FromPlayer(IPlayer player)
    {
        return new AdminActor(player.SteamID, player.Name, false, player);
    }
}
