namespace Sws2Cs2Admin.Models;

/// <summary>
/// Type of communication block.
/// </summary>
public enum CommType
{
    Mute,
    Gag,
    Silence
}

/// <summary>
/// One persisted communication block.
/// </summary>
public sealed class CommRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ulong SteamId { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public CommType Type { get; set; }

    public string Reason { get; set; } = string.Empty;

    public ulong AdminSteamId { get; set; }

    public string AdminName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedBy { get; set; }

    public string? RevokeReason { get; set; }

    public bool IsActive(DateTimeOffset now)
    {
        return RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
    }

    public bool IsPermanent => ExpiresAt is null;
}

/// <summary>
/// Serialized comms file.
/// </summary>
public sealed class CommDocument
{
    public int Version { get; set; } = 1;

    public List<CommRecord> Records { get; set; } = [];
}
