namespace Sws2Cs2Admin.Configuration;

/// <summary>
/// Contents of admins.jsonc.
/// </summary>
public sealed class AdminRoster
{
    /// <summary>Admins keyed by any SteamID format accepted by SwiftlyS2.</summary>
    public Dictionary<string, AdminEntry> Admins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Groups keyed by group name.</summary>
    public Dictionary<string, AdminGroup> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One admin entry.
/// </summary>
public sealed class AdminEntry
{
    /// <summary>Display name used in logs and menus.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Group names assigned to this admin.</summary>
    public List<string> Groups { get; set; } = [];

    /// <summary>Permission nodes granted directly to this admin.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>Explicit immunity. 0 falls back to the highest group immunity.</summary>
    public int Immunity { get; set; }
}

/// <summary>
/// One admin group.
/// </summary>
public sealed class AdminGroup
{
    /// <summary>Immunity granted by this group.</summary>
    public int Immunity { get; set; }

    /// <summary>Permission nodes granted by this group.</summary>
    public List<string> Permissions { get; set; } = [];
}
