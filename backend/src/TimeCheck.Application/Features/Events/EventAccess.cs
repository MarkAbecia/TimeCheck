using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Events;

/// <summary>Marking progress for the people the caller marks (leader: their groups; admin: everyone expected).</summary>
public sealed record MarkingDto(int Expected, int Marked, int Present);

public sealed record EventSummaryDto(
    Guid Id, string TypeCode, string TypeName, EventCategory Category, Guid? GroupId, string? GroupName, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateOnly OccursOn, string? Location, bool IsCancelled, bool IsChurchWide,
    AttendanceStatus? MyStatus, MarkingDto? Marking);

public sealed record RosterEntryDto(
    Guid UserId, string DisplayName, Guid? GroupId, string? GroupName, AttendanceStatus Status, string? Note, uint? RowVersion,
    DateTimeOffset? MarkedAt);

public sealed record EventDetailDto(
    Guid Id, Guid EventTypeId, string TypeCode, string TypeName, EventCategory Category, Guid? GroupId, string? GroupName,
    string Title, string? Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateOnly OccursOn, string? Location,
    string? OnlineLink, Guid? SeriesId, bool IsGenerated, bool IsCancelled, bool IsChurchWide,
    IReadOnlyList<Guid> AudienceGroupIds, IReadOnlyList<Guid> AudienceUserIds,
    bool CanManage, bool CanMark, bool CanEditMarks, AttendanceStatus? MyStatus, IReadOnlyList<RosterEntryDto>? Roster);

internal static class EventAccess
{
    public static async Task<(TimeZoneInfo Tz, DateOnly Today, int EditWindowDays)> ChurchClock(
        IAppDbContext db, Guid churchId, TimeProvider clock, CancellationToken ct)
    {
        var church = await db.Churches.AsNoTracking().FirstAsync(c => c.Id == churchId, ct);
        var tz = church.GetTimeZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        return (tz, today, EditWindow(church.Settings));
    }

    private static int EditWindow(string settings)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(settings);
        return doc.RootElement.TryGetProperty("attendanceEditWindowDays", out var v) && v.TryGetInt32(out var days) ? days : 7;
    }

    /// <summary>
    /// Events a user may see: admins all; others their group's and led groups' events, church-wide events, and events whose
    /// audience names them or one of those groups. Unknown ids are 404 elsewhere, never 403 (§8.1).
    /// </summary>
    public static IQueryable<Event> VisibleTo(IAppDbContext db, CurrentUserInfo me)
    {
        var q = db.Events.AsNoTracking();
        if (me.IsAdmin()) return q;
        var groups = me.LedGroupIds.Concat(me.GroupId is { } g ? [g] : []).ToList();
        var uid = me.AppUserId;
        return q.Where(e =>
            (e.GroupId != null && groups.Contains(e.GroupId.Value)) ||
            (e.GroupId == null && (!db.EventAudiences.Any(a => a.EventId == e.Id) ||
                db.EventAudiences.Any(a => a.EventId == e.Id && (a.UserId == uid || (a.GroupId != null && groups.Contains(a.GroupId.Value)))))));
    }

    /// <summary>Everyone expected at the event: its group, else its audience, else the whole church.</summary>
    public static IQueryable<Guid> Expected(IAppDbContext db, Event e, bool hasAudience)
    {
        if (e.GroupId is { } g)
            return db.GroupMemberships.Where(m => m.GroupId == g && m.LeftOn == null).Select(m => m.UserId);
        if (hasAudience)
        {
            var groups = db.EventAudiences.Where(a => a.EventId == e.Id && a.GroupId != null).Select(a => a.GroupId!.Value);
            return db.GroupMemberships.Where(m => m.LeftOn == null && groups.Contains(m.GroupId)).Select(m => m.UserId)
                .Union(db.EventAudiences.Where(a => a.EventId == e.Id && a.UserId != null).Select(a => a.UserId!.Value));
        }
        return db.Users.Where(u => u.DeactivatedAt == null).Select(u => u.Id);
    }

    /// <summary>The part of the expected people this caller marks: admins everyone, leaders the members of groups they lead.</summary>
    public static IQueryable<Guid> Markable(IAppDbContext db, CurrentUserInfo me, IQueryable<Guid> expected)
    {
        if (me.IsAdmin()) return expected;
        var led = me.LedGroupIds.ToList();
        return expected.Where(id => db.GroupMemberships.Any(m => m.UserId == id && m.LeftOn == null && led.Contains(m.GroupId)));
    }

    /// <summary>Edit and cancel: admins anything; leaders their own group's events that aren't admin-scheduled.</summary>
    public static bool CanManage(CurrentUserInfo me, Event e, EventType type) =>
        me.IsAdmin() || (e.GroupId is { } g && me.LedGroupIds.Contains(g) && type.Category != EventCategory.ADMIN_SCHEDULED);

    /// <summary>Leaders mark their own members at any event they can see; admins mark everyone (§5.1).</summary>
    public static bool CanMark(CurrentUserInfo me) => me.Role.IsAtLeast(UserRole.LEADER);

    public static string Display(string first, string last, string? nickname) =>
        string.IsNullOrWhiteSpace(nickname) ? $"{first} {last}" : nickname;

    public static async Task<Event> LoadVisible(IAppDbContext db, CurrentUserInfo me, Guid id, CancellationToken ct) =>
        await VisibleTo(db, me).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException();

    /// <summary>
    /// Summaries with the caller's own status and, for leaders/admins, how far marking got. Expected counts are per event,
    /// so this is meant for bounded lists (a few weeks).
    /// </summary>
    public static async Task<List<EventSummaryDto>> Summaries(IAppDbContext db, CurrentUserInfo me, List<Event> events, CancellationToken ct)
    {
        if (events.Count == 0) return [];
        var ids = events.Select(e => e.Id).ToList();
        var types = await db.EventTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var groupIds = events.Where(e => e.GroupId != null).Select(e => e.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var withAudience = (await db.EventAudiences.AsNoTracking().Where(a => ids.Contains(a.EventId)).Select(a => a.EventId).Distinct().ToListAsync(ct)).ToHashSet();
        var mine = await db.Attendance.AsNoTracking().Where(a => a.UserId == me.AppUserId && ids.Contains(a.EventId))
            .ToDictionaryAsync(a => a.EventId, a => a.Status, ct);

        var result = new List<EventSummaryDto>(events.Count);
        foreach (var e in events)
        {
            MarkingDto? marking = null;
            if (CanMark(me) && e.CancelledAt is null)
            {
                var markable = Markable(db, me, Expected(db, e, withAudience.Contains(e.Id)));
                var expected = await markable.CountAsync(ct);
                var marks = await db.Attendance.Where(a => a.EventId == e.Id && a.Status != AttendanceStatus.UNMARKED && markable.Contains(a.UserId))
                    .GroupBy(a => a.Status == AttendanceStatus.PRESENT || a.Status == AttendanceStatus.LATE)
                    .Select(g => new { Present = g.Key, Count = g.Count() }).ToListAsync(ct);
                if (expected > 0)
                    marking = new MarkingDto(expected, marks.Sum(m => m.Count), marks.Where(m => m.Present).Sum(m => m.Count));
            }
            var type = types[e.EventTypeId];
            result.Add(new EventSummaryDto(e.Id, type.Code, type.Name, type.Category, e.GroupId,
                e.GroupId is { } g ? groupNames.GetValueOrDefault(g) : null, e.Title, e.StartsAt, e.EndsAt, e.OccursOn, e.Location,
                e.CancelledAt is not null, e.GroupId is null && !withAudience.Contains(e.Id), mine.TryGetValue(e.Id, out var s) ? s : null, marking));
        }
        return result;
    }
}
