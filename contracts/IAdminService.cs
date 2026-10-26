using SwiftlyS2.Shared.Players;

namespace Sws2Cs2Admin.Contracts;

/// <summary>
/// Resolved admin information for one SteamID.
/// </summary>
public sealed record AdminInfo(
    ulong SteamId,
    string Name,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Permissions,
    int Immunity);

/// <summary>
/// Shared admin API. Other plugins resolve this through
/// <c>IInterfaceManager.TryGetSharedInterface&lt;IAdminService&gt;(AdminServiceConstants.InterfaceKey, ...)</c>.
/// </summary>
public interface IAdminService
{
    /// <summary>Whether the service is loaded and usable.</summary>
    bool IsAvailable { get; }

    /// <summary>Raised after the admin roster is reloaded.</summary>
    event EventHandler? Reloaded;

    /// <summary>Whether the SteamID is configured as an admin or has an admin permission node.</summary>
    bool IsAdmin(ulong steamId);

    /// <summary>Checks a permission node using admin groups, direct permissions and SwiftlyS2 permissions.</summary>
    bool HasPermission(ulong steamId, string node);

    /// <summary>Checks a permission node for an online player.</summary>
    bool HasPermission(IPlayer player, string node);

    /// <summary>Gets the configured immunity for an admin, or 0 for non-admins.</summary>
    int GetImmunity(ulong steamId);

    /// <summary>Gets the resolved admin record, or null for a SteamID that is only granted by SwiftlyS2 permissions.</summary>
    AdminInfo? GetAdmin(ulong steamId);

    /// <summary>Gets every permission node currently resolved for the SteamID.</summary>
    IReadOnlyCollection<string> GetPermissions(ulong steamId);
}
