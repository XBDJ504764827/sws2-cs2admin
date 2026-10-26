using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Sws2Cs2Admin.Models;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// JSON-backed ban history.
/// </summary>
internal sealed class BanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly string _path;
    private readonly object _sync = new();
    private readonly List<BanRecord> _bans = [];

    public BanStore(ISwiftlyCore core)
    {
        _core = core;
        _logger = core.Logger;
        _path = Path.Combine(core.PluginDataDirectory, "bans.json");
        Load();
    }

    public IReadOnlyList<BanRecord> Active()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _bans.Where(ban => ban.IsActive(now)).OrderByDescending(ban => ban.CreatedAt).ToArray();
        }
    }

    public IReadOnlyList<BanRecord> History(ulong? steamId, int limit = 20)
    {
        lock (_sync)
        {
            return _bans
                .Where(ban => !steamId.HasValue || ban.SteamId == steamId.Value)
                .OrderByDescending(ban => ban.CreatedAt)
                .Take(Math.Max(1, limit))
                .ToArray();
        }
    }

    public BanRecord? FindActive(ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _bans
                .Where(ban => ban.SteamId == steamId && ban.IsActive(now))
                .OrderByDescending(ban => ban.CreatedAt)
                .FirstOrDefault();
        }
    }

    public BanRecord Add(BanRecord ban)
    {
        lock (_sync)
        {
            _bans.Add(ban);
            SaveLocked();
        }

        return ban;
    }

    public bool Revoke(ulong steamId, string revokedBy, string? reason)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        lock (_sync)
        {
            foreach (var ban in _bans.Where(ban => ban.SteamId == steamId && ban.IsActive(now)))
            {
                ban.RevokedAt = now;
                ban.RevokedBy = revokedBy;
                ban.RevokeReason = reason;
                changed = true;
            }

            if (changed)
            {
                SaveLocked();
            }
        }

        return changed;
    }

    public void Save()
    {
        lock (_sync)
        {
            SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            var document = JsonSerializer.Deserialize<BanDocument>(json, JsonOptions);
            if (document?.Bans != null)
            {
                _bans.AddRange(document.Bans);
            }

            _logger.LogInformation("[sws2-cs2admin] Loaded {Count} ban record(s)", _bans.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to load bans from {Path}", _path);
            TryBackupCorruptFile();
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? _core.PluginDataDirectory);
            var tempPath = _path + ".tmp";
            var json = JsonSerializer.Serialize(new BanDocument { Bans = _bans }, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to save bans to {Path}", _path);
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                var backup = $"{_path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                File.Move(_path, backup);
                _logger.LogWarning("[sws2-cs2admin] Moved corrupt ban file to {Path}", backup);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[sws2-cs2admin] Failed to back up corrupt ban file");
        }
    }
}
