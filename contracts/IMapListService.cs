namespace Sws2Cs2Admin.Contracts;

/// <summary>
/// Provides access to the server's map list for map management.
/// Registered through SwiftlyS2's shared interface manager.
/// </summary>
public interface IMapListService
{
    /// <summary>The key used to register and resolve this service.</summary>
    public static readonly string InterfaceKey = "sws2-cs2admin.maplist";

    /// <summary>Gets the list of available maps.</summary>
    IReadOnlyList<string> GetAvailableMaps();

    /// <summary>Changes the server map. Raises an exception if the map is invalid.</summary>
    void ChangeMap(string mapName);
}
