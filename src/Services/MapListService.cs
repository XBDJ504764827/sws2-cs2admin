using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Manages the server's map list and handles map changes.
/// </summary>
internal sealed class MapListService : IMapListService
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private AdminSettings _settings;

    public MapListService(ISwiftlyCore core, AdminSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _settings = settings;
    }

    public void ApplySettings(AdminSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<string> GetAvailableMaps()
    {
        return _settings.Maps.Available
            .Where(map => !string.IsNullOrWhiteSpace(map))
            .Select(map => map.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ChangeMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            throw new ArgumentException("Map name cannot be empty.");
        }

        if (!_core.Engine.IsMapValid(mapName))
        {
            _logger.LogWarning("[sws2-cs2admin] Map not found or invalid: {Map}", mapName);
            throw new InvalidOperationException($"Map not found or invalid: {mapName}");
        }

        _core.Engine.ExecuteCommand($"changelevel {mapName}");
        _logger.LogInformation("[sws2-cs2admin] Map changed to {Map}", mapName);
    }
}
