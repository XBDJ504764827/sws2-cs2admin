using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Sws2Cs2Admin.Configuration;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Writes one JSON object per line to a daily audit log in the plugin data directory.
/// </summary>
internal sealed class AuditLogger : IDisposable
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private AdminAuditSettings _settings;
    private bool _disposed;

    public AuditLogger(ISwiftlyCore core, AdminAuditSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _settings = settings;
        PruneOldFiles();
    }

    public void ApplySettings(AdminAuditSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
        }

        PruneOldFiles();
    }

    public void Log(
        AdminActor actor,
        string action,
        ulong? targetSteamId = null,
        string? targetName = null,
        string? details = null,
        bool success = true)
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            action,
            success,
            actor = new
            {
                steamId = actor.SteamId,
                name = actor.Name,
                console = actor.IsConsole
            },
            target = targetSteamId.HasValue
                ? new
                {
                    steamId = targetSteamId.Value,
                    name = targetName
                }
                : null,
            details
        };

        var line = JsonSerializer.Serialize(entry);

        try
        {
            lock (_sync)
            {
                var path = GetLogPath();
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[sws2-cs2admin] Failed to append audit entry");
        }
    }

    public string GetLogDirectory()
    {
        return _core.PluginDataDirectory;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private string GetLogPath()
    {
        var directory = _core.PluginDataDirectory;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"audit-{DateTimeOffset.UtcNow:yyyy-MM-dd}.log");
    }

    private void PruneOldFiles()
    {
        if (_settings.RetentionDays <= 0)
        {
            return;
        }

        try
        {
            var directory = _core.PluginDataDirectory;
            if (!Directory.Exists(directory))
            {
                return;
            }

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.RetentionDays);
            foreach (var file in Directory.GetFiles(directory, "audit-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _logger.LogWarning(exception, "[sws2-cs2admin] Failed to prune old audit logs");
            }
        }
    }
}
