using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Invites;

/// <summary>
/// Retention (§6.5, §8.4): erases guests never converted once their purge date passes. Runs across churches in the Worker,
/// so tenant filters are off and every query names its church.
/// </summary>
public sealed class InvitePurge(IAppDbContext db, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var erased = 0;
        var now = clock.GetUtcNow();
        foreach (var church in await db.Churches.AsNoTracking().ToListAsync(ct))
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, church.GetTimeZone()).DateTime);
            var due = await db.Invites.IgnoreQueryFilters()
                .Where(i => i.ChurchId == church.Id && i.DeletedAt == null && i.ConvertedUserId == null && i.PurgeAfter < today)
                .Take(1000).ToListAsync(ct);
            foreach (var i in due) i.Erase(now);
            erased += due.Count;
        }
        await db.SaveChangesAsync(ct);
        return erased;
    }
}

/// <summary>
/// "Did your guest come?" (§6.7): once a day, to members with guests still EXPECTED at an event that ended 2 hours to 3 days ago.
/// </summary>
public sealed class InviteReminders(IAppDbContext db, TimeProvider clock)
{
    public const string Type = "invite.reminder";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var sent = 0;
        var now = clock.GetUtcNow();
        var (endedBefore, endedAfter) = (now.AddHours(-2).ToUniversalTime(), now.AddDays(-3).ToUniversalTime());
        foreach (var church in await db.Churches.AsNoTracking().ToListAsync(ct))
        {
            var tz = church.GetTimeZone();
            var local = TimeZoneInfo.ConvertTime(now, tz);
            var startOfDay = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
            var due = await db.Invites.IgnoreQueryFilters()
                .Where(i => i.ChurchId == church.Id && i.DeletedAt == null && i.Status == InviteStatus.EXPECTED)
                .Where(i => db.Events.IgnoreQueryFilters().Any(e => e.Id == i.EventId && e.CancelledAt == null && e.EndsAt <= endedBefore && e.EndsAt >= endedAfter))
                .Where(i => !db.Notifications.IgnoreQueryFilters().Any(n => n.UserId == i.InviterId && n.Type == Type && n.CreatedAt >= startOfDay))
                .GroupBy(i => i.InviterId).Select(g => new { UserId = g.Key, Guests = g.Count() })
                .ToListAsync(ct);
            foreach (var x in due)
                db.Notifications.Add(Notification.Create(church.Id, x.UserId, Type, JsonSerializer.Serialize(new { guests = x.Guests }), ["in_app"], now));
            sent += due.Count;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
