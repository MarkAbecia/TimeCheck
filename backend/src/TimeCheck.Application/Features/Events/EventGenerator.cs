using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Events;

/// <summary>
/// Creates the system's recurring events ahead of time (§5.2): Morning Prayer per group Mon–Sat, and the weekly church-wide
/// events (Week of Fire, Sunday Cell Celebration) once each, tracked per group through their rosters (§15 Q3).
/// Idempotent: existing occurrences are skipped, and the unique index on (event_type_id, group_id, occurs_on) backs that up.
/// Covers every church whatever the caller's tenant scope, so each query names its church_id and skips the tenant filter.
/// </summary>
public sealed class EventGenerator(IAppDbContext db, TimeProvider clock)
{
    public const int DaysAhead = 14;

    public async Task<int> GenerateAsync(int days = DaysAhead, CancellationToken ct = default)
    {
        var created = 0;
        foreach (var church in await db.Churches.AsNoTracking().IgnoreQueryFilters().ToListAsync(ct))
            created += await ForChurch(church, days, ct);
        await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<int> ForChurch(Church church, int days, CancellationToken ct)
    {
        var tz = church.GetTimeZone();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        var last = today.AddDays(days - 1);

        var types = (await db.EventTypes.AsNoTracking().IgnoreQueryFilters().Where(t => t.ChurchId == church.Id && t.IsActive).ToListAsync(ct))
            .Where(t => t.IsGeneratedCadence && !t.IsImplicit && t.DefaultStart is not null && t.DefaultEnd is not null)
            .ToList();
        if (types.Count == 0) return 0;

        var groups = await db.Groups.AsNoTracking().IgnoreQueryFilters().Where(g => g.ChurchId == church.Id && g.ArchivedAt == null).Select(g => g.Id).ToListAsync(ct);
        var typeIds = types.Select(t => t.Id).ToList();
        var existing = (await db.Events.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.ChurchId == church.Id && e.IsGenerated && typeIds.Contains(e.EventTypeId) && e.OccursOn >= today && e.OccursOn <= last)
                .Select(e => new { e.EventTypeId, e.GroupId, e.OccursOn })
                .ToListAsync(ct))
            .Select(e => (e.EventTypeId, e.GroupId, e.OccursOn))
            .ToHashSet();

        var created = 0;
        for (var day = today; day <= last; day = day.AddDays(1))
        {
            foreach (var type in types.Where(t => t.OccursOn(day)))
            {
                var targets = type.DefaultScope == EventScope.GROUP ? groups.Select(g => (Guid?)g) : [null];
                foreach (var groupId in targets)
                {
                    if (!existing.Add((type.Id, groupId, day))) continue;
                    var (starts, ends) = Window(day, type.DefaultStart!.Value, type.DefaultEnd!.Value, tz);
                    db.Events.Add(Event.Create(type, groupId, type.Name, starts, ends, tz, createdBy: null, isGenerated: true));
                    created++;
                }
            }
        }
        return created;
    }

    /// <summary>Local wall-clock times on a local date; an end at or before the start runs past midnight.</summary>
    internal static (DateTimeOffset Starts, DateTimeOffset Ends) Window(DateOnly day, TimeOnly start, TimeOnly end, TimeZoneInfo tz)
    {
        var s = day.ToDateTime(start);
        var e = (end > start ? day : day.AddDays(1)).ToDateTime(end);
        return (new DateTimeOffset(s, tz.GetUtcOffset(s)), new DateTimeOffset(e, tz.GetUtcOffset(e)));
    }
}
