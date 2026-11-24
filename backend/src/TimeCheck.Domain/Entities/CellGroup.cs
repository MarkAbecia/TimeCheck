using TimeCheck.Domain.Common;

namespace TimeCheck.Domain.Entities;

public sealed class CellGroup : BaseEntity, IChurchScoped, IAuditedEntity
{
    private CellGroup() { }
    public Guid ChurchId { get; private set; }
    public string Name { get; private set; } = "";
    public Guid? LeaderId { get; private set; }
    public Guid? ParentGroupId { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static CellGroup Create(Guid churchId, string name, Guid? leaderId, Guid? parentGroupId, Guid createdBy) => new()
    {
        ChurchId = churchId,
        Name = name.Trim(),
        LeaderId = leaderId,
        ParentGroupId = parentGroupId,
        CreatedBy = createdBy,
        UpdatedBy = createdBy,
    };

    public void Rename(string name, Guid actorId) { Name = name.Trim(); UpdatedBy = actorId; }
    public void AssignLeader(Guid? leaderId, Guid actorId) { LeaderId = leaderId; UpdatedBy = actorId; }
    public void Archive(DateTimeOffset at, Guid actorId) { ArchivedAt ??= at; UpdatedBy = actorId; }
}

/// <summary>Membership history: rows are closed (LeftOn), never overwritten.</summary>
public sealed class GroupMembership : BaseEntity, IChurchScoped
{
    private GroupMembership() { }
    public Guid ChurchId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly JoinedOn { get; private set; }
    public DateOnly? LeftOn { get; private set; }

    public static GroupMembership Start(Guid churchId, Guid groupId, Guid userId, DateOnly on) => new()
    {
        ChurchId = churchId,
        GroupId = groupId,
        UserId = userId,
        JoinedOn = on,
    };

    public void End(DateOnly on)
    {
        if (LeftOn is not null) throw new DomainException("membership.closed", "Membership already ended.");
        LeftOn = on < JoinedOn ? JoinedOn : on;
    }
}
