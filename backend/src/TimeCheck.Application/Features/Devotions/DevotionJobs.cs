using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Devotions;

/// <summary>
/// Scores pending devotions (Worker, every minute, so a score shows within 60 s; §6.3). Detection never blocks or fails an upload:
/// a devotion stays PENDING through provider errors and becomes FAILED after <see cref="GiveUpAfter"/>. Runs across churches
/// (the Worker has no tenant), so every query filters by church explicitly.
/// </summary>
public sealed class DevotionChecker(IAppDbContext db, IAiContentDetector detector, IFileStorage storage, TimeProvider clock, ILogger<DevotionChecker> logger)
{
    public const int BatchSize = 50;
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(1);
    /// <summary>Consecutive provider errors that end the run: a crude circuit breaker, so an outage isn't hammered every minute.</summary>
    public const int BreakAfter = 3;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var pending = await db.Devotions.IgnoreQueryFilters()
            .Where(d => d.AiStatus == AiStatus.PENDING)
            .OrderBy(d => d.SubmittedAt).Take(BatchSize)
            .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        var churchIds = pending.Select(d => d.ChurchId).Distinct().ToList();
        var settings = (await db.Churches.AsNoTracking().Where(c => churchIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => DevotionSettings.Read(c.Settings));

        var done = 0;
        var errors = 0;
        foreach (var d in pending)
        {
            try
            {
                await Check(d, settings[d.ChurchId], ct);
                await db.SaveChangesAsync(ct);
                done++;
                errors = 0;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The member replaced the content while it was being scored: the new content is PENDING and gets its own turn.
                db.DiscardChanges();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.DiscardChanges();
                logger.LogWarning(ex, "AI detection failed for devotion {DevotionId}", d.Id); // never the content (§8.4)
                if (clock.GetUtcNow() - d.SubmittedAt > GiveUpAfter)
                {
                    var stuck = await db.Devotions.IgnoreQueryFilters().FirstAsync(x => x.Id == d.Id && x.ChurchId == d.ChurchId, ct);
                    stuck.MarkAiFailed();
                    await db.SaveChangesAsync(ct);
                }
                if (++errors >= BreakAfter) break;
            }
        }
        return done;
    }

    private async Task Check(Devotion d, DevotionSettings s, CancellationToken ct)
    {
        if (!s.AiEnabled || !detector.Enabled) { d.MarkAiSkipped(); return; }

        // Same content already scored (any member, this church): reuse it instead of paying again.
        var known = await db.Devotions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ChurchId == d.ChurchId && x.ContentHash == d.ContentHash && x.AiStatus == AiStatus.DONE)
            .Select(x => new { x.AiTextScore, x.AiImageScore, x.AiProvider, x.AiModelVersion })
            .FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        if (known is not null)
        {
            d.RecordAiResult(known.AiTextScore, known.AiImageScore, known.AiProvider ?? "cache", known.AiModelVersion ?? "", now);
            return;
        }

        var text = d.TextContent is null ? null : await detector.ScoreTextAsync(d.TextContent, ct);
        AiScore? image = null;
        if (d.ImagePath is not null)
        {
            // shortcut: no OCR of handwriting yet (§6.3); the image score covers photos until a provider with OCR is chosen (ADR 0006).
            var bytes = await storage.DownloadAsync(DevotionAccess.Bucket, d.ImagePath, ct);
            image = await detector.ScoreImageAsync(bytes, "image/jpeg", ct);
        }
        if (text is null && image is null) { d.MarkAiSkipped(); return; }
        var by = text ?? image!;
        d.RecordAiResult(text?.Score, image?.Score, by.Provider, by.Model, now);
    }
}

/// <summary>
/// In-app + email reminders for open slots not yet submitted (§6.3, §6.6), at each church's reminder times. Runs every 15 minutes;
/// a reminder goes out once per person per slot per day, also when the Worker was down at the exact time and the slot is still open.
/// </summary>
public sealed class DevotionReminders(IAppDbContext db, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var sent = 0;
        var now = clock.GetUtcNow();
        foreach (var church in await db.Churches.AsNoTracking().ToListAsync(ct))
        {
            var s = DevotionSettings.Read(church.Settings);
            var tz = church.GetTimeZone();
            var local = TimeZoneInfo.ConvertTime(now, tz).DateTime;
            var today = DateOnly.FromDateTime(local);
            var startOfDay = DevotionAccess.ToInstant(today.ToDateTime(TimeOnly.MinValue), tz).ToUniversalTime();

            foreach (var (slot, at) in new[] { (DevotionSlot.DAY, s.DayReminder), (DevotionSlot.NIGHT, s.NightReminder) })
            {
                if (TimeOnly.FromDateTime(local) < at || !s.Windows.IsOpen(slot, today, local)) continue;
                var type = slot == DevotionSlot.DAY ? "devotion.reminder.day" : "devotion.reminder.night";
                var people = await db.Users.IgnoreQueryFilters()
                    .Where(u => u.ChurchId == church.Id && u.DeletedAt == null && u.DeactivatedAt == null)
                    .Where(u => !db.Devotions.IgnoreQueryFilters().Any(d => d.UserId == u.Id && d.DevotionDate == today && d.Slot == slot))
                    .Where(u => !db.Notifications.IgnoreQueryFilters().Any(n => n.UserId == u.Id && n.Type == type && n.CreatedAt >= startOfDay))
                    .Select(u => u.Id)
                    .ToListAsync(ct);
                var payload = JsonSerializer.Serialize(new { date = today, slot = slot.ToString() });
                foreach (var id in people)
                    db.Notifications.Add(Notification.Create(church.Id, id, type, payload, ["in_app", "email"], now));
                sent += people.Count;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
