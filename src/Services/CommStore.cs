using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Sws2Cs2Admin.Models;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// JSON-backed mute, gag and silence records.
/// </summary>
internal sealed class CommStore
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
    private readonly List<CommRecord> _records = [];

    public CommStore(ISwiftlyCore core)
    {
        _core = core;
        _logger = core.Logger;
        _path = Path.Combine(core.PluginDataDirectory, "comms.json");
        Load();
    }

    public IReadOnlyList<CommRecord> Active()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _records.Where(record => record.IsActive(now)).OrderByDescending(record => record.CreatedAt).ToArray();
        }
    }

    public IReadOnlyList<CommRecord> Expired(DateTimeOffset now)
    {
        lock (_sync)
        {
            return _records
                .Where(record => record.RevokedAt is null && record.ExpiresAt.HasValue && record.ExpiresAt <= now)
                .ToArray();
        }
    }

    public IReadOnlyList<CommRecord> ActiveFor(ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _records
                .Where(record => record.SteamId == steamId && record.IsActive(now))
                .OrderByDescending(record => record.CreatedAt)
                .ToArray();
        }
    }

    public bool IsMuted(ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _records.Any(record => record.SteamId == steamId
                && record.IsActive(now)
                && record.Type is CommType.Mute or CommType.Silence);
        }
    }

    public bool IsGagged(ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            return _records.Any(record => record.SteamId == steamId
                && record.IsActive(now)
                && record.Type is CommType.Gag or CommType.Silence);
        }
    }

    public CommRecord Add(CommRecord record)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            foreach (var existing in _records.Where(item => item.SteamId == record.SteamId
                && item.Type == record.Type
                && item.IsActive(now)))
            {
                existing.RevokedAt = now;
                existing.RevokedBy = "system";
                existing.RevokeReason = "Replaced by a new block";
            }

            _records.Add(record);
            SaveLocked();
        }

        return record;
    }

    public bool Revoke(ulong steamId, CommType type, string revokedBy, string? reason)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        lock (_sync)
        {
            foreach (var record in _records.Where(item => item.SteamId == steamId
                && item.Type == type
                && item.IsActive(now)))
            {
                record.RevokedAt = now;
                record.RevokedBy = revokedBy;
                record.RevokeReason = reason;
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
            var document = JsonSerializer.Deserialize<CommDocument>(json, JsonOptions);
            if (document?.Records != null)
            {
                _records.AddRange(document.Records);
            }

            _logger.LogInformation("[sws2-cs2admin] Loaded {Count} comm record(s)", _records.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to load comms from {Path}", _path);
            TryBackupCorruptFile();
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? _core.PluginDataDirectory);
            var tempPath = _path + ".tmp";
            var json = JsonSerializer.Serialize(new CommDocument { Records = _records }, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to save comms to {Path}", _path);
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
                _logger.LogWarning("[sws2-cs2admin] Moved corrupt comms file to {Path}", backup);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[sws2-cs2admin] Failed to back up corrupt comms file");
        }
    }
}
