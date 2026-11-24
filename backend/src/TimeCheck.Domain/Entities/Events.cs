using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class EventType : BaseEntity, IChurchScoped
{
    public const string MorningPrayer = "MORNING_PRAYER";

    private EventType() { }
    public Guid ChurchId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public EventCategory Category { get; private set; }
    public EventCadence Cadence { get; private set; }
    public EventScope DefaultScope { get; private set; }
    public bool IsSystem { get; private set; }
    /// <summary>0 = Sunday. Used by WEEKLY generation.</summary>
    public short? Weekday { get; private set; }
    public TimeOnly? DefaultStart { get; private set; }
    public TimeOnly? DefaultEnd { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static EventType Create(Guid churchId, string code, string name, EventCategory category,
        EventCadence cadence, EventScope scope, short? weekday = null) => new()
        {
            ChurchId = churchId,
            Code = code,
            Name = name,
            Category = category,
            Cadence = cadence,
            DefaultScope = scope,
            Weekday = weekday,
        };

    /// <summary>Created by the recurring job rather than by people (Morning Prayer, Week of Fire, Sunday Celebration; devotions are implicit).</summary>
    public bool IsGeneratedCadence => Cadence is EventCadence.DAILY or EventCadence.DAILY_MON_SAT || (Cadence == EventCadence.WEEKLY && Weekday is not null);

    /// <summary>Devotions are attendance by upload (Phase 7), never events.</summary>
    public bool IsImplicit => DefaultScope == EventScope.INDIVIDUAL;

    /// <summary>Should a generated occurrence exist on this local date? (§5.1, §5.2)</summary>
    public bool OccursOn(DateOnly localDate) => Cadence switch
    {
        EventCadence.DAILY => true,
        EventCadence.DAILY_MON_SAT => localDate.DayOfWeek != DayOfWeek.Sunday,
        EventCadence.WEEKLY => Weekday is { } wd && (int)localDate.DayOfWeek == wd,
        _ => false,
    };
}

public sealed class Event : BaseEntity, IChurchScoped, IAuditedEntity
{
    private Event() { }
    public Guid ChurchId { get; private set; }
    public Guid EventTypeId { get; private set; }
    public Guid? GroupId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateOnly OccursOn { get; private set; }
    public string? Location { get; private set; }
    public string? OnlineLink { get; private set; }
    public string? RecurrenceRule { get; private set; }
    public Guid? SeriesId { get; private set; }
    public bool IsGenerated { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static Event Create(EventType type, Guid? groupId, string title, DateTimeOffset startsAt, DateTimeOffset endsAt,
        TimeZoneInfo churchTz, Guid? createdBy, bool isGenerated = false, string? description = null,
        string? location = null, string? onlineLink = null)
    {
        if (endsAt <= startsAt) throw new DomainException("event.time", "An event must end after it starts.");
        var occursOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, churchTz).DateTime);
        if (type.Code == EventType.MorningPrayer && occursOn.DayOfWeek == DayOfWeek.Sunday)
            throw new DomainException("event.sunday", "Morning Prayer is never held on Sunday.");

        return new Event
        {
            ChurchId = type.ChurchId,
            EventTypeId = type.Id,
            GroupId = groupId,
            Title = title.Trim(),
            StartsAt = startsAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            OccursOn = occursOn,
            IsGenerated = isGenerated,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
            Description = description,
            Location = location,
            OnlineLink = onlineLink,
        };
    }

    public void Cancel(DateTimeOffset at, Guid actorId) { CancelledAt ??= at; UpdatedBy = actorId; }

    /// <summary>Type and group stay fixed; a new time moves the local date too (and Morning Prayer still can't land on Sunday).</summary>
    public void Update(EventType type, string title, string? description, DateTimeOffset startsAt, DateTimeOffset endsAt,
        string? location, string? onlineLink, TimeZoneInfo churchTz, Guid actorId)
    {
        if (CancelledAt is not null) throw new DomainException("event.cancelled", "A cancelled event can't be changed.");
        if (endsAt <= startsAt) throw new DomainException("event.time", "An event must end after it starts.");
        var occursOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, churchTz).DateTime);
        if (type.Code == EventType.MorningPrayer && occursOn.DayOfWeek == DayOfWeek.Sunday)
            throw new DomainException("event.sunday", "Morning Prayer is never held on Sunday.");
        Title = title.Trim();
        Description = description;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        OccursOn = occursOn;
        Location = location;
        OnlineLink = onlineLink;
        UpdatedBy = actorId;
    }

    /// <summary>Weekly occurrences of one Cell Meeting share a series id (§6.6 recurrence).</summary>
    public void JoinSeries(Guid seriesId, string rule) { SeriesId = seriesId; RecurrenceRule = rule; }
}

public sealed class EventAudience : BaseEntity
{
    private EventAudience() { }
    public Guid EventId { get; private set; }
    public Guid? GroupId { get; private set; }
    public Guid? UserId { get; private set; }

    public static EventAudience ForGroup(Guid eventId, Guid groupId) => new() { EventId = eventId, GroupId = groupId };
    public static EventAudience ForUser(Guid eventId, Guid userId) => new() { EventId = eventId, UserId = userId };
}

public sealed class Attendance : BaseEntity, IChurchScoped
{
    private Attendance() { }
    public Guid ChurchId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public Guid? MarkedBy { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Postgres xmin, used for optimistic concurrency.</summary>
    public uint RowVersion { get; private set; }

    public static Attendance Mark(Event evt, Guid userId, AttendanceStatus status, Guid markedBy, DateTimeOffset now, string? note = null) => new()
    {
        ChurchId = evt.ChurchId,
        EventId = evt.Id,
        UserId = userId,
        Status = status,
        MarkedBy = markedBy,
        MarkedAt = now,
        Note = note,
    };

    /// <summary>Edits are allowed for <paramref name="editWindowDays"/> after the event ends; afterwards only admins (§5.2).</summary>
    public static bool CanEdit(Event evt, DateTimeOffset now, int editWindowDays, UserRole actorRole) =>
        actorRole.IsAtLeast(UserRole.ADMIN) || now <= evt.EndsAt.AddDays(editWindowDays);

    public void Change(AttendanceStatus status, Guid markedBy, DateTimeOffset now, string? note)
    {
        Status = status;
        MarkedBy = markedBy;
        MarkedAt = now;
        Note = note;
    }
}
