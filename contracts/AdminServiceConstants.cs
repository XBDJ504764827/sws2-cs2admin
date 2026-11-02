namespace Sws2Cs2Admin.Contracts;

/// <summary>
/// Constants shared by the admin service and its consumers.
/// </summary>
public static class AdminServiceConstants
{
    /// <summary>
    /// Key used to register and resolve <see cref="IAdminService"/> through SwiftlyS2's
    /// shared interface manager.
    /// </summary>
    public const string InterfaceKey = "sws2-cs2admin";
}

/// <summary>
/// Permission nodes used by the admin plugin. Consumers should use these constants
/// instead of hard-coding strings.
/// </summary>
public static class AdminPermissions
{
    /// <summary>Grants every admin permission through wildcard matching.</summary>
    public const string All = "sws2.admin.*";

    /// <summary>Open the admin menu.</summary>
    public const string Menu = "sws2.admin.menu";

    /// <summary>Kick a player.</summary>
    public const string Kick = "sws2.admin.kick";

    /// <summary>Slay a player.</summary>
    public const string Slay = "sws2.admin.slay";

    /// <summary>Damage a player with a slap.</summary>
    public const string Slap = "sws2.admin.slap";

    /// <summary>Respawn a player.</summary>
    public const string Respawn = "sws2.admin.respawn";

    /// <summary>Freeze a player.</summary>
    public const string Freeze = "sws2.admin.freeze";

    /// <summary>Mute voice chat.</summary>
    public const string Mute = "sws2.admin.mute";

    /// <summary>Gag text chat.</summary>
    public const string Gag = "sws2.admin.gag";

    /// <summary>Mute and gag a player.</summary>
    public const string Silence = "sws2.admin.silence";

    /// <summary>Ban a player or SteamID.</summary>
    public const string Ban = "sws2.admin.ban";

    /// <summary>Lift a ban.</summary>
    public const string Unban = "sws2.admin.unban";

    /// <summary>View ban records.</summary>
    public const string BansView = "sws2.admin.bans.view";

    /// <summary>View admin information.</summary>
    public const string Who = "sws2.admin.who";

    /// <summary>Reload the admin roster.</summary>
    public const string Reload = "sws2.admin.reload";

    /// <summary>View audit information.</summary>
    public const string Audit = "sws2.admin.audit";

    /// <summary>Ignore immunity checks when targeting another admin.</summary>
    public const string ImmunityBypass = "sws2.admin.immunity.bypass";
}
