using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Features.Events;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Notifications;

/// <summary>
/// The outbox (§6.7): sends what other features queued, to the channels each person chose. In-app needs nothing sent.
/// Email is retried with backoff; push goes out on the first try only, so a retried email never repeats a push.
/// </summary>
public sealed partial class NotificationDelivery(IAppDbContext db, IEmailSender email, IPushSender push, TimeProvider clock, ILogger<NotificationDelivery> log)
{
    public const int BatchSize = 100;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.DeliveredAt == null && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt).Take(BatchSize).ToListAsync(ct);
        if (due.Count == 0) return 0;

        // Claim first, so an overlapping run skips these. shortcut: two runs reading in the same instant could both claim; a row lock (FOR UPDATE SKIP LOCKED) fixes that if it ever matters.
        foreach (var n in due) n.Claim(now.AddMinutes(5));
        await db.SaveChangesAsync(ct);

        var userIds = due.Select(n => n.UserId).Distinct().ToList();
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.Settings, u.ChurchId, Gone = u.DeletedAt != null || u.DeactivatedAt != null })
            .ToDictionaryAsync(u => u.Id, ct);
        var subs = (await db.PushSubscriptions.IgnoreQueryFilters().Where(s => userIds.Contains(s.UserId)).ToListAsync(ct))
            .ToLookup(s => s.UserId);
        var zones = (await db.Churches.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id, c => c.GetTimeZone());

        foreach (var n in due)
        {
            if (!users.TryGetValue(n.UserId, out var u) || u.Gone)
            {
                n.Delivered([NotificationCatalog.InApp], now);
                continue;
            }
            var prefs = NotificationPreferences.Read(u.Settings)[NotificationCatalog.Category(n.Type)];
            var text = NotificationCatalog.Render(n.Type, n.Payload, zones[n.ChurchId]);
            var sent = new List<string> { NotificationCatalog.InApp };
            string? error = null;

            if (prefs.Email && email.Enabled)
            {
                try
                {
                    await email.SendAsync(Email(u.Email, text), ct);
                    sent.Add(NotificationCatalog.Email);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    error = $"email: {e.Message}";
                    LogEmailFailed(log, e, n.Id);
                }
            }

            if (prefs.Push && push.Enabled && n.Attempts == 0)
            {
                var payload = JsonSerializer.Serialize(new { title = text.Title, body = text.Body, url = text.Url, tag = n.Id });
                foreach (var s in subs[n.UserId])
                {
                    var outcome = await push.SendAsync(s.Endpoint, s.P256dh, s.Auth, payload, ct);
                    if (outcome == PushOutcome.Gone) db.PushSubscriptions.Remove(s);
                    if (outcome == PushOutcome.Sent && !sent.Contains(NotificationCatalog.Push)) sent.Add(NotificationCatalog.Push);
                }
            }

            if (error is null) n.Delivered(sent, now);
            else n.Failed(error, now);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private EmailMessage Email(string to, RenderedNotification text)
    {
        var link = $"{email.AppUrl.TrimEnd('/')}{text.Url}";
        var settings = $"{email.AppUrl.TrimEnd('/')}/profile";
        static string h(string s) => WebUtility.HtmlEncode(s);
        return new EmailMessage(to, text.Title,
            $"{text.Body}\n\nOpen TimeCheck: {link}\n\nChoose which emails you get: {settings}\n",
            $"<p>{h(text.Body)}</p><p><a href=\"{h(link)}\">Open TimeCheck</a></p>" +
            $"<p style=\"color:#666;font-size:13px\"><a href=\"{h(settings)}\">Choose which emails you get</a></p>");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email for notification {NotificationId} failed; it will be retried")]
    private static partial void LogEmailFailed(ILogger logger, Exception e, Guid notificationId);
}

/// <summary>
/// "Not marked yet" (§6.7): two hours after an event ends, each leader with members still unmarked hears once per event.
/// Looks back two days, so a missed run catches up.
/// </summary>
public sealed class AttendanceReminders(IAppDbContext db, TimeProvider clock)
{
    public const string Type = "attendance.unmarked";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (endedBefore, endedAfter) = (now.AddHours(-2).ToUniversalTime(), now.AddDays(-2).ToUniversalTime());
        var events = await db.Events.IgnoreQueryFilters()
            .Where(e => e.CancelledAt == null && e.EndsAt <= endedBefore && e.EndsAt >= endedAfter).ToListAsync(ct);
        var sent = 0;
        foreach (var e in events)
        {
            var hasAudience = await db.EventAudiences.IgnoreQueryFilters().AnyAsync(a => a.EventId == e.Id, ct);
            var expected = EventAccess.Expected(db, e, hasAudience);
            var unmarked = expected.Where(id => !db.Attendance.IgnoreQueryFilters()
                .Any(a => a.EventId == e.Id && a.UserId == id && a.Status != AttendanceStatus.UNMARKED));
            // Each unmarked person's leader: the leader of the group they're in now.
            var byLeader = await (
                from m in db.GroupMemberships.IgnoreQueryFilters()
                join g in db.Groups.IgnoreQueryFilters() on m.GroupId equals g.Id
                where m.LeftOn == null && g.ChurchId == e.ChurchId && g.LeaderId != null && unmarked.Contains(m.UserId)
                group m by g.LeaderId into x
                select new { LeaderId = x.Key!.Value, Members = x.Count() }).ToListAsync(ct);
            if (byLeader.Count == 0) continue;

            var leaders = byLeader.Select(x => x.LeaderId).ToList();
            var told = (await db.Notifications.IgnoreQueryFilters()
                    .Where(n => n.Type == Type && leaders.Contains(n.UserId) && n.CreatedAt >= e.EndsAt).Select(n => new { n.UserId, n.Payload })
                    .ToListAsync(ct))
                .Where(n => JsonDocument.Parse(n.Payload).RootElement.TryGetProperty("eventId", out var id) && id.GetString() == e.Id.ToString())
                .Select(n => n.UserId).ToHashSet();
            foreach (var x in byLeader.Where(x => !told.Contains(x.LeaderId)))
            {
                var payload = JsonSerializer.Serialize(new { eventId = e.Id, title = e.Title, startsAt = e.StartsAt, members = x.Members });
                db.Notifications.Add(Notification.Create(e.ChurchId, x.LeaderId, Type, payload, [NotificationCatalog.InApp], now));
                sent++;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
