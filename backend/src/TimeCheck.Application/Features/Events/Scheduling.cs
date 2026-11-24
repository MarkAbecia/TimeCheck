using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Events;

// ---------------------------------------------------------------- Event types

/// <summary>Types people can schedule. Generated and implicit types are listed too, flagged, so filters can name them.</summary>
public sealed record EventTypeDto(Guid Id, string Code, string Name, EventCategory Category, EventScope DefaultScope, bool Schedulable, bool LeadersMaySchedule);

public sealed record ListEventTypes : IRequest<IReadOnlyList<EventTypeDto>>;

public sealed class ListEventTypesHandler(IAppDbContext db) : IRequestHandler<ListEventTypes, IReadOnlyList<EventTypeDto>>
{
    public async Task<IReadOnlyList<EventTypeDto>> Handle(ListEventTypes r, CancellationToken ct) =>
        (await db.EventTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync(ct))
        .Select(t => new EventTypeDto(t.Id, t.Code, t.Name, t.Category, t.DefaultScope, Scheduling.Schedulable(t), Scheduling.LeadersMay(t)))
        .ToList();
}

internal static class Scheduling
{
    public static bool Schedulable(EventType t) => !t.IsGeneratedCadence && !t.IsImplicit;
    public static bool LeadersMay(EventType t) => Schedulable(t) && t.Category != EventCategory.ADMIN_SCHEDULED;

    /// <summary>Outbox rows for everyone expected (Phase 9 delivers them). The actor isn't told about their own change.</summary>
    public static Task Notify(IAppDbContext db, Event e, bool hasAudience, string type, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        Notify(db, e, EventAccess.Expected(db, e, hasAudience), type, actorId, now, ct);

    public static async Task Notify(IAppDbContext db, Event e, IQueryable<Guid> expected, string type, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var people = await expected.Where(id => id != actorId).ToListAsync(ct);
        var payload = JsonSerializer.Serialize(new { eventId = e.Id, title = e.Title, startsAt = e.StartsAt }, Json);
        foreach (var userId in people)
            db.Notifications.Add(Notification.Create(e.ChurchId, userId, type, payload, ["in_app", "email"], now));
    }

    /// <summary>Overlapping, live events for the same group, or church-wide ones when this one is church-wide.</summary>
    public static async Task<List<EventConflict>> Conflicts(IAppDbContext db, Guid? groupId, DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? exceptId, CancellationToken ct)
    {
        // Npgsql takes timestamptz parameters in UTC only; clients send their local offset.
        var (starts, ends) = (startsAt.ToUniversalTime(), endsAt.ToUniversalTime());
        return await db.Events.AsNoTracking()
            .Where(e => e.CancelledAt == null && e.Id != exceptId && e.GroupId == groupId && e.StartsAt < ends && e.EndsAt > starts)
            .OrderBy(e => e.StartsAt).Take(5)
            .Select(e => new EventConflict(e.Id, e.Title, e.StartsAt, e.EndsAt))
            .ToListAsync(ct);
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Rules<T>(AbstractValidator<T> v, Func<T, string?> title, Func<T, string?> description, Func<T, DateTimeOffset> starts,
        Func<T, DateTimeOffset> ends, Func<T, string?> location, Func<T, string?> link)
    {
        v.RuleFor(x => title(x)).MaximumLength(200).OverridePropertyName("title");
        v.RuleFor(x => description(x)).MaximumLength(4000).OverridePropertyName("description");
        v.RuleFor(x => location(x)).MaximumLength(300).OverridePropertyName("location");
        v.RuleFor(x => link(x)).MaximumLength(2000)
            .Must(l => l is null || (Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Use a full https:// link.").OverridePropertyName("onlineLink");
        v.RuleFor(x => ends(x)).Must((x, e) => e > starts(x)).WithMessage("The end has to be after the start.").OverridePropertyName("endsAt");
        v.RuleFor(x => ends(x)).Must((x, e) => e - starts(x) <= TimeSpan.FromDays(14)).WithMessage("An event can last at most 14 days.").OverridePropertyName("endsAt");
    }
}

// ---------------------------------------------------------------- List (§6.2 admin view, leader past/upcoming)

public sealed record ListEvents(DateOnly From, DateOnly To, Guid? GroupId = null, Guid? TypeId = null, EventCategory? Category = null,
    bool IncludeCancelled = false) : IRequest<IReadOnlyList<EventSummaryDto>>;

public sealed class ListEventsValidator : AbstractValidator<ListEvents>
{
    public ListEventsValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date can't be before the start date.");
        // shortcut: a bounded date range instead of keyset paging; the window keeps lists small. Page if longer ranges are needed.
        RuleFor(x => x.To).Must((x, to) => to.DayNumber - x.From.DayNumber <= 62).WithMessage("Choose at most about two months.");
    }
}

public sealed class ListEventsHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<ListEvents, IReadOnlyList<EventSummaryDto>>
{
    public async Task<IReadOnlyList<EventSummaryDto>> Handle(ListEvents r, CancellationToken ct)
    {
        var me = current.Info;
        var q = EventAccess.VisibleTo(db, me).Where(e => e.OccursOn >= r.From && e.OccursOn <= r.To);
        if (!r.IncludeCancelled) q = q.Where(e => e.CancelledAt == null);
        // A group's view includes the church-wide events its members attend.
        if (r.GroupId is { } g) q = q.Where(e => e.GroupId == g || e.GroupId == null);
        if (r.TypeId is { } t) q = q.Where(e => e.EventTypeId == t);
        if (r.Category is { } c) q = q.Where(e => db.EventTypes.Any(t => t.Id == e.EventTypeId && t.Category == c));
        var events = await q.OrderBy(e => e.StartsAt).ThenBy(e => e.Id).Take(500).ToListAsync(ct);
        return await EventAccess.Summaries(db, me, events, ct);
    }
}

// ---------------------------------------------------------------- Today (§6.2 leader view)

public sealed record TodayDto(DateOnly Date, IReadOnlyList<EventSummaryDto> Today, IReadOnlyList<EventSummaryDto> Upcoming, int UnmarkedPast);

public sealed record GetMyToday : IRequest<TodayDto>;

public sealed class GetMyTodayHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<GetMyToday, TodayDto>
{
    public async Task<TodayDto> Handle(GetMyToday r, CancellationToken ct)
    {
        var me = current.Info;
        var (_, today, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        var from = today.AddDays(-14);
        var events = await EventAccess.VisibleTo(db, me).Where(e => e.CancelledAt == null && e.OccursOn >= from && e.OccursOn <= today.AddDays(7))
            .OrderBy(e => e.StartsAt).ToListAsync(ct);
        var all = await EventAccess.Summaries(db, me, events, ct);
        // Daily events would crowd "coming up"; they're on the list each morning anyway.
        var upcoming = all.Where(e => e.OccursOn > today && e.Category != EventCategory.DAILY).Take(5).ToList();
        var unmarked = all.Count(e => e.OccursOn < today && e.Marking is { } m && m.Marked < m.Expected);
        return new TodayDto(today, all.Where(e => e.OccursOn == today).ToList(), upcoming, unmarked);
    }
}

// ---------------------------------------------------------------- Detail + roster

public sealed record GetEvent(Guid Id, Guid? GroupId = null) : IRequest<EventDetailDto>;

public sealed class GetEventRequestHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<GetEvent, EventDetailDto>
{
    public async Task<EventDetailDto> Handle(GetEvent r, CancellationToken ct)
    {
        var me = current.Info;
        var e = await EventAccess.LoadVisible(db, me, r.Id, ct);
        return await EventDetail.Build(db, me, e, r.GroupId, clock, ct);
    }
}

internal static class EventDetail
{
    public static async Task<EventDetailDto> Build(IAppDbContext db, CurrentUserInfo me, Event e, Guid? groupFilter, TimeProvider clock, CancellationToken ct)
    {
        var type = await db.EventTypes.AsNoTracking().FirstAsync(t => t.Id == e.EventTypeId, ct);
        var audience = await db.EventAudiences.AsNoTracking().Where(a => a.EventId == e.Id).ToListAsync(ct);
        var groupName = e.GroupId is { } g ? await db.Groups.AsNoTracking().Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var (_, _, window) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        var myStatus = await db.Attendance.AsNoTracking().Where(a => a.EventId == e.Id && a.UserId == me.AppUserId).Select(a => (AttendanceStatus?)a.Status).FirstOrDefaultAsync(ct);

        List<RosterEntryDto>? roster = null;
        var canMark = EventAccess.CanMark(me) && e.CancelledAt is null;
        if (canMark)
        {
            var ids = EventAccess.Markable(db, me, EventAccess.Expected(db, e, audience.Count > 0));
            if (groupFilter is { } gf) ids = ids.Where(id => db.GroupMemberships.Any(m => m.UserId == id && m.LeftOn == null && m.GroupId == gf));
            var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .Select(u => new
                {
                    u.Id, u.FirstName, u.LastName, u.Nickname,
                    Group = db.GroupMemberships.Where(m => m.UserId == u.Id && m.LeftOn == null)
                        .Join(db.Groups, m => m.GroupId, x => x.Id, (m, x) => new { x.Id, x.Name }).FirstOrDefault(),
                })
                .ToListAsync(ct);
            var marks = await db.Attendance.AsNoTracking().Where(a => a.EventId == e.Id && ids.Contains(a.UserId)).ToDictionaryAsync(a => a.UserId, ct);
            roster = people
                .Select(p =>
                {
                    var m = marks.GetValueOrDefault(p.Id);
                    return new RosterEntryDto(p.Id, EventAccess.Display(p.FirstName, p.LastName, p.Nickname), p.Group?.Id, p.Group?.Name,
                        m?.Status ?? AttendanceStatus.UNMARKED, m?.Note, m?.RowVersion, m?.MarkedAt);
                })
                .OrderBy(p => p.GroupName).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new EventDetailDto(e.Id, type.Id, type.Code, type.Name, type.Category, e.GroupId, groupName, e.Title, e.Description,
            e.StartsAt, e.EndsAt, e.OccursOn, e.Location, e.OnlineLink, e.SeriesId, e.IsGenerated, e.CancelledAt is not null,
            e.GroupId is null && audience.Count == 0,
            audience.Where(a => a.GroupId != null).Select(a => a.GroupId!.Value).ToList(),
            audience.Where(a => a.UserId != null).Select(a => a.UserId!.Value).ToList(),
            EventAccess.CanManage(me, e, type), canMark, canMark && Attendance.CanEdit(e, clock.GetUtcNow(), window, me.Role), myStatus, roster);
    }
}

// ---------------------------------------------------------------- Create (§6.6)

public sealed record CreateEvent(
    Guid EventTypeId, Guid? GroupId, string? Title, string? Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string? Location, string? OnlineLink, IReadOnlyList<Guid>? AudienceGroupIds, IReadOnlyList<Guid>? AudienceUserIds,
    int RepeatWeeks = 1, bool ConfirmConflicts = false) : IRequest<Guid>, IAuditedRequest
{
    public string AuditAction => "event.create";
}

public sealed class CreateEventValidator : AbstractValidator<CreateEvent>
{
    public CreateEventValidator()
    {
        Scheduling.Rules(this, x => x.Title, x => x.Description, x => x.StartsAt, x => x.EndsAt, x => x.Location, x => x.OnlineLink);
        RuleFor(x => x.RepeatWeeks).InclusiveBetween(1, 26);
        RuleFor(x => x.AudienceGroupIds).Must(a => a is null || a.Count <= 100);
        RuleFor(x => x.AudienceUserIds).Must(a => a is null || a.Count <= 500);
    }
}

public sealed class CreateEventRequestHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<CreateEvent, Guid>
{
    /// <summary>Returns the (first) event id; the endpoint reads the detail back once it is saved.</summary>
    public async Task<Guid> Handle(CreateEvent r, CancellationToken ct)
    {
        var me = current.Info;
        var type = await db.EventTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.EventTypeId && t.IsActive, ct) ?? throw new NotFoundException();
        if (!Scheduling.Schedulable(type))
            throw new DomainException("event.generated", $"{type.Name} is added automatically and can't be scheduled by hand.");

        var groupScoped = type.DefaultScope == EventScope.GROUP;
        if (groupScoped && r.GroupId is null)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("groupId", $"Choose the group for this {type.Name}.")]);
        if (r.GroupId is { } g)
        {
            if (!me.CanManageGroup(g) || !await db.Groups.AnyAsync(x => x.Id == g && x.ArchivedAt == null, ct)) throw new NotFoundException();
        }
        if (!me.IsAdmin() && (!Scheduling.LeadersMay(type) || r.GroupId is null))
            throw new ForbiddenException("Only admins schedule church-wide events.");
        if (r.RepeatWeeks > 1 && type.Code != "CELL_MEETING")
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("repeatWeeks", "Only Cell Meetings repeat weekly.")]);

        var audienceGroups = r.GroupId is null ? (r.AudienceGroupIds ?? []).Distinct().ToList() : [];
        var audienceUsers = r.GroupId is null ? (r.AudienceUserIds ?? []).Distinct().ToList() : [];
        if (audienceGroups.Count > 0 && await db.Groups.CountAsync(x => audienceGroups.Contains(x.Id) && x.ArchivedAt == null, ct) != audienceGroups.Count)
            throw new NotFoundException();
        if (audienceUsers.Count > 0 && await db.Users.CountAsync(u => audienceUsers.Contains(u.Id), ct) != audienceUsers.Count)
            throw new NotFoundException();

        if (!r.ConfirmConflicts)
        {
            var conflicts = await Scheduling.Conflicts(db, r.GroupId, r.StartsAt, r.EndsAt, null, ct);
            if (conflicts.Count > 0) throw new EventConflictWarningException(conflicts);
        }

        var (tz, _, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        var title = string.IsNullOrWhiteSpace(r.Title) ? type.Name : r.Title;
        var series = r.RepeatWeeks > 1 ? Guid.NewGuid() : (Guid?)null;
        Event? first = null;
        for (var week = 0; week < r.RepeatWeeks; week++)
        {
            // Same local wall-clock time each week, even across a DST change.
            var starts = Shift(r.StartsAt, week, tz);
            var e = Event.Create(type, r.GroupId, title, starts, starts + (r.EndsAt - r.StartsAt), tz, me.AppUserId,
                description: r.Description, location: r.Location, onlineLink: r.OnlineLink);
            if (series is { } s) e.JoinSeries(s, $"FREQ=WEEKLY;COUNT={r.RepeatWeeks}");
            db.Events.Add(e);
            foreach (var ag in audienceGroups) db.EventAudiences.Add(EventAudience.ForGroup(e.Id, ag));
            foreach (var au in audienceUsers) db.EventAudiences.Add(EventAudience.ForUser(e.Id, au));
            first ??= e;
        }

        // One notice per person for the whole series. The audience isn't saved yet, so who to tell comes from the request.
        IQueryable<Guid> expected = r.GroupId is { } eg
            ? db.GroupMemberships.Where(m => m.GroupId == eg && m.LeftOn == null).Select(m => m.UserId)
            : audienceGroups.Count + audienceUsers.Count == 0
                ? db.Users.Where(u => u.DeactivatedAt == null).Select(u => u.Id)
                : db.GroupMemberships.Where(m => m.LeftOn == null && audienceGroups.Contains(m.GroupId)).Select(m => m.UserId)
                    .Union(db.Users.Where(u => audienceUsers.Contains(u.Id)).Select(u => u.Id));
        await Scheduling.Notify(db, first!, expected, "event.scheduled", me.AppUserId, clock.GetUtcNow(), ct);
        audit.Describe("event", first!.Id, null, new { type = type.Code, groupId = r.GroupId, startsAt = first.StartsAt, weeks = r.RepeatWeeks, seriesId = series });
        return first.Id;
    }

    private static DateTimeOffset Shift(DateTimeOffset start, int weeks, TimeZoneInfo tz)
    {
        if (weeks == 0) return start;
        var local = TimeZoneInfo.ConvertTime(start, tz).DateTime.AddDays(7 * weeks);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }
}

// ---------------------------------------------------------------- Edit / cancel

public sealed record UpdateEvent(
    Guid Id, string? Title, string? Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? OnlineLink,
    bool ConfirmConflicts = false) : IRequest<EventDetailDto>, IAuditedRequest
{
    public string AuditAction => "event.update";
}

public sealed class UpdateEventValidator : AbstractValidator<UpdateEvent>
{
    public UpdateEventValidator() =>
        Scheduling.Rules(this, x => x.Title, x => x.Description, x => x.StartsAt, x => x.EndsAt, x => x.Location, x => x.OnlineLink);
}

public sealed class UpdateEventRequestHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<UpdateEvent, EventDetailDto>
{
    public async Task<EventDetailDto> Handle(UpdateEvent r, CancellationToken ct)
    {
        var me = current.Info;
        var visible = await EventAccess.LoadVisible(db, me, r.Id, ct);
        var e = await db.Events.FirstAsync(x => x.Id == visible.Id, ct);
        var type = await db.EventTypes.AsNoTracking().FirstAsync(t => t.Id == e.EventTypeId, ct);
        if (!EventAccess.CanManage(me, e, type)) throw new ForbiddenException("Only the group's leader or an admin can change this event.");

        var timeChanged = e.StartsAt != r.StartsAt.ToUniversalTime() || e.EndsAt != r.EndsAt.ToUniversalTime();
        if (timeChanged && !r.ConfirmConflicts)
        {
            var conflicts = await Scheduling.Conflicts(db, e.GroupId, r.StartsAt, r.EndsAt, e.Id, ct);
            if (conflicts.Count > 0) throw new EventConflictWarningException(conflicts);
        }

        var before = new { e.Title, e.StartsAt, e.EndsAt, e.Location };
        var (tz, _, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        e.Update(type, string.IsNullOrWhiteSpace(r.Title) ? type.Name : r.Title, r.Description, r.StartsAt, r.EndsAt, r.Location, r.OnlineLink, tz, me.AppUserId);
        if (timeChanged)
            await Scheduling.Notify(db, e, await db.EventAudiences.AnyAsync(a => a.EventId == e.Id, ct), "event.changed", me.AppUserId, clock.GetUtcNow(), ct);
        audit.Describe("event", e.Id, before, new { e.Title, e.StartsAt, e.EndsAt, e.Location });
        return await EventDetail.Build(db, me, e, null, clock, ct);
    }
}

public sealed record CancelEvent(Guid Id) : IRequest<EventDetailDto>, IAuditedRequest
{
    public string AuditAction => "event.cancel";
}

public sealed class CancelEventRequestHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<CancelEvent, EventDetailDto>
{
    public async Task<EventDetailDto> Handle(CancelEvent r, CancellationToken ct)
    {
        var me = current.Info;
        var visible = await EventAccess.LoadVisible(db, me, r.Id, ct);
        var e = await db.Events.FirstAsync(x => x.Id == visible.Id, ct);
        var type = await db.EventTypes.AsNoTracking().FirstAsync(t => t.Id == e.EventTypeId, ct);
        if (!EventAccess.CanManage(me, e, type)) throw new ForbiddenException("Only the group's leader or an admin can cancel this event.");
        if (e.CancelledAt is not null) return await EventDetail.Build(db, me, e, null, clock, ct);

        // Notify before cancelling: the roster is everyone who expected to be there (§6.6).
        await Scheduling.Notify(db, e, await db.EventAudiences.AnyAsync(a => a.EventId == e.Id, ct), "event.cancelled", me.AppUserId, clock.GetUtcNow(), ct);
        await Invites.InviteAccess.NotifyCancelled(db, e, me.AppUserId, clock.GetUtcNow(), ct);
        e.Cancel(clock.GetUtcNow(), me.AppUserId);
        audit.Describe("event", e.Id, new { cancelled = false }, new { cancelled = true });
        return await EventDetail.Build(db, me, e, null, clock, ct);
    }
}
