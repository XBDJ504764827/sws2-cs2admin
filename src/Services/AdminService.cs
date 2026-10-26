using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SteamAPI;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;

namespace Sws2Cs2Admin.Services;

/// <summary>
/// Loads admins.jsonc and answers permission and immunity queries.
/// </summary>
internal sealed class AdminService : IAdminService
{
    private sealed class ResolvedAdmin
    {
        public required ulong SteamId { get; init; }

        public required string Name { get; init; }

        public required IReadOnlyList<string> Groups { get; init; }

        public required HashSet<string> Permissions { get; init; }

        public required int Immunity { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<ulong, ResolvedAdmin> _admins = [];

    private AdminRoster _roster = new();
    private AdminSettings _settings;
    private volatile bool _available;

    public AdminService(ISwiftlyCore core, AdminSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _settings = settings;
        _available = true;
    }

    public bool IsAvailable => _available;

    public event EventHandler? Reloaded;

    public void ApplySettings(AdminSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            RebuildLocked();
        }
    }

    public bool Reload()
    {
        var path = _core.Configuration.GetConfigPath("admins.jsonc");

        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("[sws2-cs2admin] Admin roster not found: {Path}", path);
                return false;
            }

            var json = File.ReadAllText(path);
            var roster = JsonSerializer.Deserialize<AdminRoster>(json, JsonOptions) ?? new AdminRoster();

            lock (_sync)
            {
                _roster = roster;
                RebuildLocked();
            }

            var count = _admins.Count;
            _logger.LogInformation("[sws2-cs2admin] Loaded {Count} admin(s) from {Path}", count, path);

            try
            {
                Reloaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "[sws2-cs2admin] AdminService.Reloaded handler threw");
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[sws2-cs2admin] Failed to reload admin roster from {Path}", path);
            return false;
        }
    }

    public bool IsAdmin(ulong steamId)
    {
        if (steamId == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_admins.ContainsKey(steamId))
            {
                return true;
            }
        }

        return _settings.UseSwiftlyPermissions
            && _core.Permission.GetPlayerPermissions(steamId)
                .Any(permission => permission.StartsWith("sws2.admin", StringComparison.OrdinalIgnoreCase));
    }

    public bool HasPermission(ulong steamId, string node)
    {
        if (string.IsNullOrWhiteSpace(node))
        {
            return true;
        }

        lock (_sync)
        {
            if (_admins.TryGetValue(steamId, out var admin)
                && PermissionMatcher.Matches(admin.Permissions, node))
            {
                return true;
            }

            if (!_settings.UseSwiftlyPermissions)
            {
                return false;
            }
        }

        return _core.Permission.PlayerHasPermission(steamId, node);
    }

    public bool HasPermission(IPlayer player, string node)
    {
        return HasPermission(player.SteamID, node);
    }

    public int GetImmunity(ulong steamId)
    {
        lock (_sync)
        {
            return _admins.TryGetValue(steamId, out var admin) ? admin.Immunity : 0;
        }
    }

    public AdminInfo? GetAdmin(ulong steamId)
    {
        lock (_sync)
        {
            if (!_admins.TryGetValue(steamId, out var admin))
            {
                return null;
            }

            return new AdminInfo(
                admin.SteamId,
                admin.Name,
                admin.Groups,
                admin.Permissions.OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase).ToArray(),
                admin.Immunity);
        }
    }

    public IReadOnlyCollection<string> GetPermissions(ulong steamId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            if (_admins.TryGetValue(steamId, out var admin))
            {
                result.UnionWith(admin.Permissions);
            }

            if (!_settings.UseSwiftlyPermissions)
            {
                return result;
            }
        }

        foreach (var permission in _core.Permission.GetPlayerPermissions(steamId))
        {
            result.Add(permission);
        }

        return result;
    }

    public bool CanTarget(AdminActor actor, IPlayer target)
    {
        if (actor.IsConsole)
        {
            return true;
        }

        if (target.SteamID == 0 || !IsAdmin(target.SteamID))
        {
            return true;
        }

        if (actor.SteamId == target.SteamID)
        {
            return true;
        }

        if (HasPermission(actor.SteamId, AdminPermissions.ImmunityBypass))
        {
            return true;
        }

        return GetImmunity(actor.SteamId) > GetImmunity(target.SteamID);
    }

    public void Shutdown()
    {
        _available = false;
        lock (_sync)
        {
            _admins.Clear();
        }
    }

    private void RebuildLocked()
    {
        _admins.Clear();

        var groups = _roster.Groups ?? new Dictionary<string, AdminGroup>(StringComparer.OrdinalIgnoreCase);
        var entries = _roster.Admins ?? new Dictionary<string, AdminEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (identity, entry) in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var steamId = SteamIdParser.ParseToSteamId64(identity);
            if (!steamId.HasValue)
            {
                _logger.LogWarning("[sws2-cs2admin] Ignoring admin with invalid SteamID: {Identity}", identity);
                continue;
            }

            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolvedGroups = new List<string>();
            var immunity = Math.Max(0, entry.Immunity);

            foreach (var groupName in entry.Groups ?? [])
            {
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    continue;
                }

                var group = groups
                    .FirstOrDefault(pair => pair.Key.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                    .Value;

                if (group is null)
                {
                    _logger.LogWarning(
                        "[sws2-cs2admin] Admin {Identity} references unknown group '{Group}'",
                        identity,
                        groupName);
                    continue;
                }

                resolvedGroups.Add(groupName);
                immunity = Math.Max(immunity, group.Immunity);
                permissions.UnionWith(group.Permissions ?? []);
            }

            permissions.UnionWith(entry.Permissions ?? []);
            immunity = Math.Max(immunity, _settings.DefaultImmunity);

            _admins[steamId.Value] = new ResolvedAdmin
            {
                SteamId = steamId.Value,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? identity : entry.Name,
                Groups = resolvedGroups,
                Permissions = permissions,
                Immunity = immunity
            };
        }
    }
}
