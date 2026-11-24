using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class Invite : BaseEntity, IChurchScoped
{
    private Invite() { }
    public Guid ChurchId { get; private set; }
    public Guid InviterId { get; private set; }
    public Guid EventId { get; private set; }
    public string GuestName { get; private set; } = "";
    /// <summary>Encrypted at rest.</summary>
    public string? GuestContact { get; private set; }
    public InviteStatus Status { get; private set; } = InviteStatus.EXPECTED;
    public Guid? ConvertedUserId { get; private set; }
    public DateOnly PurgeAfter { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
}

/// <summary>Outbox row: written in the same transaction as the change; delivered by the Worker.</summary>
public sealed class Notification : BaseEntity, IChurchScoped
{
    private Notification() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = "";
    public string Payload { get; private set; } = "{}";
    public IReadOnlyList<string> Channels { get; private set; } = ["in_app"];
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public static Notification Create(Guid churchId, Guid userId, string type, string payloadJson, IReadOnlyList<string> channels, DateTimeOffset now) => new()
    {
        ChurchId = churchId,
        UserId = userId,
        Type = type,
        Payload = payloadJson,
        Channels = channels,
        NextAttemptAt = now,
    };
}

public sealed class ReportJob : BaseEntity, IChurchScoped
{
    private ReportJob() { }
    public Guid ChurchId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Type { get; private set; } = "";
    public string Params { get; private set; } = "{}";
    public ReportJobStatus Status { get; private set; } = ReportJobStatus.QUEUED;
    public string? FilePath { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
}
