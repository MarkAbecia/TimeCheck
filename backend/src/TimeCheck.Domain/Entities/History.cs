using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class RoleChange : BaseEntity, IChurchScoped
{
    private RoleChange() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public UserRole FromRole { get; private set; }
    public UserRole ToRole { get; private set; }
    public Guid ChangedBy { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    internal static RoleChange Record(Guid churchId, Guid userId, UserRole from, UserRole to, Guid by, string? reason) => new()
    {
        ChurchId = churchId,
        UserId = userId,
        FromRole = from,
        ToRole = to,
        ChangedBy = by,
        Reason = reason,
        ChangedAt = DateTimeOffset.UtcNow,
    };
}

public sealed class MemberStatusChange : BaseEntity, IChurchScoped
{
    private MemberStatusChange() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public MemberStatus FromStatus { get; private set; }
    public MemberStatus ToStatus { get; private set; }
    public Guid ChangedBy { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    internal static MemberStatusChange Record(Guid churchId, Guid userId, MemberStatus from, MemberStatus to, Guid by, string? reason) => new()
    {
        ChurchId = churchId,
        UserId = userId,
        FromStatus = from,
        ToStatus = to,
        ChangedBy = by,
        Reason = reason,
        ChangedAt = DateTimeOffset.UtcNow,
    };
}

/// <summary>Append-only. Written by the application pipeline, never edited.</summary>
public sealed class AuditLog : IChurchScoped
{
    private AuditLog() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ChurchId { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = "";
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public string? IpHash { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditLog Create(Guid churchId, Guid? actorId, string action, string? entityType, string? entityId,
        string? beforeJson, string? afterJson, string? ipHash, string? userAgent, string? correlationId) => new()
        {
            ChurchId = churchId,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Before = beforeJson,
            After = afterJson,
            IpHash = ipHash,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
            CorrelationId = correlationId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
}
