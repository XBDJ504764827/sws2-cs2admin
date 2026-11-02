namespace Sws2Cs2Admin.Configuration;

/// <summary>
/// Plugin configuration bound from the "sws2-cs2admin" section of config.jsonc.
/// </summary>
public sealed class AdminSettings
{
    /// <summary>Enable debug logging.</summary>
    public bool Debug { get; set; }

    /// <summary>Also read admin nodes from SwiftlyS2's permissions.jsonc.</summary>
    public bool UseSwiftlyPermissions { get; set; } = true;

    /// <summary>Immunity used for admins without an explicit immunity.</summary>
    public int DefaultImmunity { get; set; }

    /// <summary>Ban settings.</summary>
    public AdminBanSettings Bans { get; set; } = new();

    /// <summary>Mute, gag and silence settings.</summary>
    public AdminCommsSettings Comms { get; set; } = new();

    /// <summary>Audit log settings.</summary>
    public AdminAuditSettings Audit { get; set; } = new();

    /// <summary>Admin menu settings.</summary>
    public AdminMenuSettings Menu { get; set; } = new();

    /// <summary>Admin notification settings.</summary>
    public AdminNotifySettings Notify { get; set; } = new();
}

/// <summary>
/// Ban behavior.
/// </summary>
public sealed class AdminBanSettings
{
    /// <summary>Whether join checks are enabled.</summary>
    public bool CheckOnJoin { get; set; } = true;

    /// <summary>Default ban duration in minutes. 0 means permanent.</summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>Maximum ban duration in minutes. 0 means no limit.</summary>
    public int MaxDurationMinutes { get; set; } = 525600;

    /// <summary>Whether unban is allowed.</summary>
    public bool AllowUnban { get; set; } = true;

    /// <summary>Message shown to a banned player. {0} reason, {1} expiry.</summary>
    public string KickMessage { get; set; } = "Banned: {0} ({1})";
}

/// <summary>
/// Mute, gag and silence behavior.
/// </summary>
public sealed class AdminCommsSettings
{
    /// <summary>Whether text chat gag is enforced.</summary>
    public bool EnforceGag { get; set; } = true;

    /// <summary>Default comm duration in minutes. 0 means permanent.</summary>
    public int DefaultDurationMinutes { get; set; } = 30;

    /// <summary>Maximum comm duration in minutes. 0 means no limit.</summary>
    public int MaxDurationMinutes { get; set; }

    /// <summary>How often expired comm blocks are checked, in seconds.</summary>
    public int ExpireCheckSeconds { get; set; } = 30;
}

/// <summary>
/// Audit log behavior.
/// </summary>
public sealed class AdminAuditSettings
{
    /// <summary>Whether audit logging is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Days to keep daily audit files. 0 keeps them forever.</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Admin menu behavior.
/// </summary>
public sealed class AdminMenuSettings
{
    /// <summary>Options per page, clamped to 1-5 by sws2-cs2menus.</summary>
    public int PageSize { get; set; } = 5;
}

/// <summary>
/// Admin notifications.
/// </summary>
public sealed class AdminNotifySettings
{
    /// <summary>Send successful action notices to other online admins.</summary>
    public bool OnlineAdmins { get; set; }

    /// <summary>Announce bans to everyone.</summary>
    public bool BroadcastBans { get; set; }
}
