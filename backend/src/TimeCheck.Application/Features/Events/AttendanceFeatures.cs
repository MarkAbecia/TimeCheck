using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Events;

// ---------------------------------------------------------------- Mark (§5.2, §6.2)

/// <summary>RowVersion is the one the client last saw; null for someone not marked yet.</summary>
public sealed record AttendanceMark(Guid UserId, AttendanceStatus Status, string? Note, uint? RowVersion);

/// <summary>
/// Sets the given people's status for one event ("mark all present" then the exceptions, in one request). Resending a mark that is
/// already stored is a no-op, so a phone retrying on flaky data never trips the concurrency check (§9 idempotency).
/// </summary>
public sealed record MarkAttendance(Guid EventId, IReadOnlyList<AttendanceMark> Marks) : IRequest<int>, IAuditedRequest
{
    public string AuditAction => "attendance.mark";
}

public sealed class MarkAttendanceValidator : AbstractValidator<MarkAttendance>
{
    public MarkAttendanceValidator()
    {
        RuleFor(x => x.Marks).NotEmpty().Must(m => m.Count <= 1000).WithMessage("Mark at most 1000 people at once.");
        RuleFor(x => x.Marks).Must(m => m.Select(x => x.UserId).Distinct().Count() == m.Count).WithMessage("Each person can be marked once.");
        RuleForEach(x => x.Marks).ChildRules(m =>
        {
            m.RuleFor(x => x.Status).IsInEnum();
            m.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}

public sealed class MarkAttendanceHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<MarkAttendance, int>
{
    public async Task<int> Handle(MarkAttendance r, CancellationToken ct)
    {
        var me = current.Info;
        var e = await EventAccess.LoadVisible(db, me, r.EventId, ct);
        if (e.CancelledAt is not null) throw new DomainException("event.cancelled", "This event was cancelled.");
        var now = clock.GetUtcNow();
        var (_, today, window) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        if (e.OccursOn > today) throw new DomainException("attendance.future", "You can mark attendance from the day of the event.");
        if (!Attendance.CanEdit(e, now, window, me.Role))
            throw new ForbiddenException($"Attendance closes {window} days after the event. Ask an admin to change it.");

        var hasAudience = await db.EventAudiences.AnyAsync(a => a.EventId == e.Id, ct);
        var ids = r.Marks.Select(m => m.UserId).ToList();
        var markable = await EventAccess.Markable(db, me, EventAccess.Expected(db, e, hasAudience)).Where(id => ids.Contains(id)).ToListAsync(ct);
        if (markable.Count != ids.Count) throw new ForbiddenException("Some people on this list aren't yours to mark.");

        var existing = await db.Attendance.Where(a => a.EventId == e.Id && ids.Contains(a.UserId)).ToDictionaryAsync(a => a.UserId, ct);
        var before = new List<object>();
        var after = new List<object>();
        var stale = 0;
        foreach (var m in r.Marks)
        {
            var note = string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim();
            if (existing.TryGetValue(m.UserId, out var row))
            {
                if (row.Status == m.Status && row.Note == note) continue; // already so: a retry, or two people agreeing
                if (m.RowVersion != row.RowVersion) { stale++; continue; }
                before.Add(new { userId = row.UserId, status = row.Status, note = row.Note is not null });
                row.Change(m.Status, me.AppUserId, now, note);
            }
            else
            {
                if (m.Status == AttendanceStatus.UNMARKED) continue;
                before.Add(new { userId = m.UserId, status = AttendanceStatus.UNMARKED, note = false });
                db.Attendance.Add(Attendance.Mark(e, m.UserId, m.Status, me.AppUserId, now, note));
            }
            after.Add(new { userId = m.UserId, status = m.Status, note = note is not null });
        }
        // Someone else marked these people since this list was loaded: save nothing, the client reloads and shows their marks.
        if (stale > 0) throw new DbUpdateConcurrencyException($"{stale} marks changed since you opened the list.");

        // Old and new values per person (§5.2); notes are flagged, not copied, since they can describe people.
        audit.Describe("event", e.Id, before, after);
        return after.Count;
    }
}

// ---------------------------------------------------------------- Own history (§6.2 member view)

public sealed record HistoryItemDto(Guid EventId, string TypeCode, string TypeName, string Title, DateTimeOffset StartsAt, DateOnly OccursOn, AttendanceStatus Status);

/// <summary>Rate = present or late over everything counted; excused events don't count, unmarked ones count as absent (§5.2).</summary>
public sealed record MyAttendanceDto(DateOnly From, DateOnly To, IReadOnlyList<HistoryItemDto> Items, int Attended, int Counted, int MorningPrayerStreak);

public sealed record GetMyAttendance(DateOnly? From = null, DateOnly? To = null) : IRequest<MyAttendanceDto>;

public sealed class GetMyAttendanceValidator : AbstractValidator<GetMyAttendance>
{
    public GetMyAttendanceValidator() =>
        RuleFor(x => x).Must(x => x.From is null || x.To is null || (x.To >= x.From && x.To.Value.DayNumber - x.From.Value.DayNumber <= 92))
            .WithMessage("Choose a range of at most three months.").OverridePropertyName("to");
}

public sealed class GetMyAttendanceHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<GetMyAttendance, MyAttendanceDto>
{
    public async Task<MyAttendanceDto> Handle(GetMyAttendance r, CancellationToken ct)
    {
        var me = current.Info;
        var (_, today, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        var to = r.To is { } t && t < today ? t : today;
        var from = r.From ?? to.AddDays(-29);

        var items = await Mine(from, to).ToListAsync(ct);
        var counted = items.Where(i => i.Status != AttendanceStatus.EXCUSED).ToList();
        var attended = counted.Count(i => i.Status is AttendanceStatus.PRESENT or AttendanceStatus.LATE);

        // Consecutive Morning Prayers attended, newest first. Today's one doesn't break the streak before it's marked.
        var prayers = await Mine(today.AddDays(-180), today, EventType.MorningPrayer)
            .ToListAsync(ct);
        var streak = 0;
        foreach (var p in prayers)
        {
            if (p.OccursOn == today && p.Status == AttendanceStatus.UNMARKED) continue;
            if (p.Status is AttendanceStatus.PRESENT or AttendanceStatus.LATE) streak++;
            else if (p.Status != AttendanceStatus.EXCUSED) break;
        }
        return new MyAttendanceDto(from, to, items, attended, counted.Count, streak);
    }

    /// <summary>Newest first. Events the caller was expected at (their group's, church-wide, or naming them), or was marked at, that have started.</summary>
    private IQueryable<HistoryItemDto> Mine(DateOnly from, DateOnly to, string? typeCode = null)
    {
        var me = current.Info;
        var uid = me.AppUserId;
        var g = me.GroupId;
        var now = clock.GetUtcNow();
        return db.Events.AsNoTracking()
            .Where(e => e.CancelledAt == null && e.OccursOn >= from && e.OccursOn <= to && e.StartsAt <= now)
            .Where(e => (g != null && e.GroupId == g)
                || (e.GroupId == null && (!db.EventAudiences.Any(a => a.EventId == e.Id)
                    || db.EventAudiences.Any(a => a.EventId == e.Id && (a.UserId == uid || (g != null && a.GroupId == g)))))
                || db.Attendance.Any(a => a.EventId == e.Id && a.UserId == uid))
            .Join(db.EventTypes, e => e.EventTypeId, t => t.Id, (e, t) => new { e, t })
            .Where(x => typeCode == null || x.t.Code == typeCode)
            .OrderByDescending(x => x.e.StartsAt)
            .Select(x => new HistoryItemDto(x.e.Id, x.t.Code, x.t.Name, x.e.Title, x.e.StartsAt, x.e.OccursOn,
                db.Attendance.Where(a => a.EventId == x.e.Id && a.UserId == uid).Select(a => (AttendanceStatus?)a.Status).FirstOrDefault() ?? AttendanceStatus.UNMARKED));
    }
}
