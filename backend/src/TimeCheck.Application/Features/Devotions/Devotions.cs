using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Devotions;

// ---------------------------------------------------------------- Settings and shapes

/// <summary>The church's devotion and AI settings (§6.9), read from churches.settings with the §15 defaults.</summary>
public sealed record DevotionSettings(
    DevotionWindows Windows, TimeOnly DayReminder, TimeOnly NightReminder,
    bool AiEnabled, int LikelyHumanBelow, int LikelyAiAbove, bool MembersSeeOwnScore)
{
    public static DevotionSettings Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var dev = root.TryGetProperty("devotion", out var d) ? d : default;
        var ai = root.TryGetProperty("ai", out var a) ? a : default;
        TimeOnly T(JsonElement e, string name, TimeOnly fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && TimeOnly.TryParse(v.GetString(), out var t) ? t : fallback;
        int I(string name, int fallback) => ai.ValueKind == JsonValueKind.Object && ai.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;
        bool B(string name, bool fallback) => ai.ValueKind == JsonValueKind.Object && ai.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
        var w = DevotionWindows.Default;
        return new(
            new DevotionWindows(T(dev, "dayOpens", w.DayOpens), T(dev, "dayCloses", w.DayCloses), T(dev, "nightOpens", w.NightOpens), T(dev, "nightCloses", w.NightCloses)),
            T(dev, "dayReminder", new(7, 0)), T(dev, "nightReminder", new(20, 0)),
            B("enabled", true), I("likelyHumanBelow", 40), I("likelyAiAbove", 75), B("membersSeeOwnScore", false));
    }

    /// <summary>The band label for a score (§6.3): advisory only, never acted on automatically.</summary>
    public string? Band(decimal? score) =>
        score is null ? null : score < LikelyHumanBelow ? "LIKELY_HUMAN" : score > LikelyAiAbove ? "LIKELY_AI" : "UNCERTAIN";
}

public sealed record DevotionDto(
    Guid Id, DateOnly Date, DevotionSlot Slot, string? Text, bool HasImage, DateTimeOffset SubmittedAt,
    AiStatus AiStatus, decimal? AiTextScore, decimal? AiImageScore, string? Band, bool Reviewed);

/// <summary>Closes is the last minute the slot accepts uploads (23:59 for Day), as people read it.</summary>
public sealed record SlotDto(DevotionSlot Slot, DateOnly Date, DateTimeOffset Closes, DevotionDto? Devotion);

public sealed record MyDevotionsDto(DateOnly Today, IReadOnlyList<SlotDto> Open, IReadOnlyList<DevotionDto> Recent, int Streak, bool ShowsScores);

public sealed record DevotionDetailDto(
    DevotionDto Devotion, Guid UserId, string MemberName, string? GroupName, string? ImageUrl, bool CanReview,
    Guid? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewNote, bool ShowsScores);

internal static class DevotionAccess
{
    public const string Bucket = "devotions";
    public static readonly TimeSpan SignedUrlTtl = TimeSpan.FromMinutes(5); // §6.3: ≤ 5 min

    public static async Task<(DevotionSettings Settings, TimeZoneInfo Tz, DateTime LocalNow)> Clock(
        IAppDbContext db, Guid churchId, TimeProvider clock, CancellationToken ct)
    {
        var church = await db.Churches.AsNoTracking().FirstAsync(c => c.Id == churchId, ct);
        var tz = church.GetTimeZone();
        return (DevotionSettings.Read(church.Settings), tz, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
    }

    public static DevotionDto Dto(Devotion d, DevotionSettings s, bool showScores) => new(
        d.Id, d.DevotionDate, d.Slot, d.TextContent, d.ImagePath is not null, d.SubmittedAt, d.AiStatus,
        showScores ? d.AiTextScore : null, showScores ? d.AiImageScore : null,
        showScores ? s.Band(Max(d.AiTextScore, d.AiImageScore)) : null, d.ReviewedAt is not null);

    public static decimal? Max(decimal? a, decimal? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    /// <summary>People whose devotions this user may review: their led groups' current members, or everyone for admins (§15 Q9).</summary>
    public static IQueryable<Guid> Reviewable(IAppDbContext db, CurrentUserInfo me) =>
        me.IsAdmin()
            ? db.Users.Select(u => u.Id)
            : db.GroupMemberships.Where(m => m.LeftOn == null && me.LedGroupIds.Contains(m.GroupId)).Select(m => m.UserId);

    public static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo tz) => new(local, tz.GetUtcOffset(local));
}

// ---------------------------------------------------------------- Submit (§6.3)

/// <summary>
/// Saves the member's devotion for a slot. A second upload to the same slot edits the first, so a retried upload is harmless.
/// <paramref name="Image"/> replaces the stored image; <paramref name="KeepImage"/> keeps the current one when no new image is sent.
/// </summary>
public sealed record SubmitDevotion(DateOnly Date, DevotionSlot Slot, string? Text, byte[]? Image, bool KeepImage)
    : IRequest<DevotionDto>, IAuditedRequest
{
    public string AuditAction => "devotion.submit";
    public const int MaxImageBytes = 10 * 1024 * 1024;
}

public sealed class SubmitDevotionValidator : AbstractValidator<SubmitDevotion>
{
    public SubmitDevotionValidator()
    {
        RuleFor(x => x.Slot).IsInEnum();
        RuleFor(x => x.Text).MaximumLength(Devotion.MaxTextLength);
        RuleFor(x => x.Image).Must(i => i is null || i.Length <= SubmitDevotion.MaxImageBytes).WithMessage("Images can be up to 10 MB.");
    }
}

public sealed class SubmitDevotionHandler(
    IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock, IFileStorage storage, IImageSanitizer images, UnitOfWork uow)
    : IRequestHandler<SubmitDevotion, DevotionDto>
{
    public async Task<DevotionDto> Handle(SubmitDevotion r, CancellationToken ct)
    {
        var me = current.Info;
        var (settings, tz, localNow) = await DevotionAccess.Clock(db, me.ChurchId, clock, ct);
        if (!settings.Windows.IsOpen(r.Slot, r.Date, localNow))
            throw new DomainException("devotion.closed", "This devotion slot isn't open right now.");

        var existing = await db.Devotions.FirstOrDefaultAsync(d => d.UserId == me.AppUserId && d.DevotionDate == r.Date && d.Slot == r.Slot, ct);
        var text = string.IsNullOrWhiteSpace(r.Text) ? null : r.Text.Trim();

        // The storage key is the cleaned image's hash: never user-controlled, and an unchanged image keeps its key and its score.
        string? path = null, imageHash = null;
        if (r.Image is { Length: > 0 })
        {
            var clean = images.Sanitize(r.Image);
            imageHash = Convert.ToHexStringLower(SHA256.HashData(clean.Bytes));
            path = $"{me.ChurchId}/{me.AppUserId}/{imageHash}.jpg";
            if (path != existing?.ImagePath)
            {
                await storage.UploadAsync(DevotionAccess.Bucket, path, clean.Bytes, clean.ContentType, ct);
                var uploaded = path;
                uow.OnRollback(() => storage.DeleteAsync(DevotionAccess.Bucket, uploaded, CancellationToken.None));
            }
        }
        else if (r.KeepImage && existing?.ImagePath is { } kept)
        {
            path = kept;
            imageHash = Path.GetFileNameWithoutExtension(kept);
        }

        var hash = ContentHash(text, imageHash);
        var now = clock.GetUtcNow();
        var oldPath = existing?.ImagePath;
        if (existing is null)
        {
            existing = Devotion.Submit(me.ChurchId, me.AppUserId, r.Date, r.Slot, text, path, hash, now);
            db.Devotions.Add(existing);
        }
        else existing.Replace(text, path, hash, now);

        if (oldPath is not null && oldPath != path)
            uow.AfterCommit(() => storage.DeleteAsync(DevotionAccess.Bucket, oldPath, CancellationToken.None));

        // The content itself is never logged or audited (§8.4).
        audit.Describe("devotion", existing.Id, null, new { date = r.Date, slot = r.Slot, hasText = text is not null, hasImage = path is not null });
        return DevotionAccess.Dto(existing, settings, settings.MembersSeeOwnScore);
    }

    /// <summary>SHA-256 over the text and the cleaned image, so identical content reuses an earlier score instead of re-billing (§6.3).</summary>
    public static string ContentHash(string? text, string? imageHash) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"t:{text}\ni:{imageHash}")));
}

// ---------------------------------------------------------------- Mine (§6.2 member view)

public sealed record GetMyDevotions : IRequest<MyDevotionsDto>;

public sealed class GetMyDevotionsHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<GetMyDevotions, MyDevotionsDto>
{
    public const int Days = 30;

    public async Task<MyDevotionsDto> Handle(GetMyDevotions r, CancellationToken ct)
    {
        var me = current.Info;
        var (settings, tz, localNow) = await DevotionAccess.Clock(db, me.ChurchId, clock, ct);
        var today = DateOnly.FromDateTime(localNow);
        // shortcut: the streak looks back 400 days at most; read a stored streak if someone keeps one up longer than that.
        var since = today.AddDays(-400);
        var mine = await db.Devotions.AsNoTracking()
            .Where(d => d.UserId == me.AppUserId && d.DevotionDate >= since)
            .OrderByDescending(d => d.DevotionDate).ThenByDescending(d => d.Slot)
            .ToListAsync(ct);

        var show = settings.MembersSeeOwnScore;
        var open = settings.Windows.OpenAt(localNow)
            .Select(s => new SlotDto(s.Slot, s.Date, DevotionAccess.ToInstant(settings.Windows.Window(s.Slot, s.Date).Closes.AddMinutes(-1), tz),
                mine.Where(d => d.DevotionDate == s.Date && d.Slot == s.Slot).Select(d => DevotionAccess.Dto(d, settings, show)).FirstOrDefault()))
            .ToList();
        var recent = mine.Where(d => d.DevotionDate > today.AddDays(-Days)).Select(d => DevotionAccess.Dto(d, settings, show)).ToList();
        return new(today, open, recent, Streak(mine.Select(d => d.DevotionDate).ToHashSet(), today), show);
    }

    /// <summary>Days in a row with at least one devotion, ending today (or yesterday, while today is still open).</summary>
    public static int Streak(IReadOnlySet<DateOnly> days, DateOnly today)
    {
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var n = 0;
        while (days.Contains(day)) { n++; day = day.AddDays(-1); }
        return n;
    }
}

// ---------------------------------------------------------------- One devotion

public sealed record GetDevotion(Guid Id) : IRequest<DevotionDetailDto>;

public sealed class GetDevotionHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock, IFileStorage storage)
    : IRequestHandler<GetDevotion, DevotionDetailDto>
{
    public async Task<DevotionDetailDto> Handle(GetDevotion r, CancellationToken ct)
    {
        var me = current.Info;
        var d = await db.Devotions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException();
        var own = d.UserId == me.AppUserId;
        var canReview = !own && await DevotionAccess.Reviewable(db, me).AnyAsync(id => id == d.UserId, ct);
        if (!own && !canReview) throw new NotFoundException(); // no hint that it exists

        var (settings, _, _) = await DevotionAccess.Clock(db, me.ChurchId, clock, ct);
        var show = canReview || settings.MembersSeeOwnScore;
        var who = await db.Users.AsNoTracking().Where(u => u.Id == d.UserId)
            .Select(u => new
            {
                u.FirstName, u.LastName, u.Nickname,
                Group = db.GroupMemberships.Where(m => m.UserId == u.Id && m.LeftOn == null)
                    .Join(db.Groups, m => m.GroupId, g => g.Id, (m, g) => g.Name).FirstOrDefault(),
            })
            .FirstAsync(ct);
        var url = d.ImagePath is null ? null : await storage.SignedUrlAsync(DevotionAccess.Bucket, d.ImagePath, DevotionAccess.SignedUrlTtl, ct);
        return new(DevotionAccess.Dto(d, settings, show), d.UserId, Events.EventAccess.Display(who.FirstName, who.LastName, who.Nickname), who.Group,
            url, canReview, canReview ? d.ReviewedBy : null, canReview ? d.ReviewedAt : null, canReview ? d.ReviewNote : null, show);
    }
}

// ---------------------------------------------------------------- Group review list (§6.3 display, §15 Q5)

public sealed record DevotionBriefDto(Guid Id, AiStatus AiStatus, decimal? Score, string? Band, bool Reviewed, bool HasImage);
public sealed record MemberDevotionsDto(Guid UserId, string DisplayName, string? GroupName, DevotionBriefDto? Day, DevotionBriefDto? Night);
public sealed record GroupDevotionsDto(DateOnly Date, IReadOnlyList<MemberDevotionsDto> Members, int Submitted, int Expected);

/// <summary>Who submitted on a date, with score bands. <paramref name="LikelyAiOnly"/> is the "Likely AI" filter; nothing else acts on it.</summary>
public sealed record ListGroupDevotions(DateOnly? Date, Guid? GroupId, bool LikelyAiOnly) : IRequest<GroupDevotionsDto>;

public sealed class ListGroupDevotionsHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock)
    : IRequestHandler<ListGroupDevotions, GroupDevotionsDto>
{
    public async Task<GroupDevotionsDto> Handle(ListGroupDevotions r, CancellationToken ct)
    {
        var me = current.Info;
        if (r.GroupId is { } g && !me.IsAdmin() && !me.LedGroupIds.Contains(g)) throw new NotFoundException();
        var (settings, _, localNow) = await DevotionAccess.Clock(db, me.ChurchId, clock, ct);
        var date = r.Date ?? DateOnly.FromDateTime(localNow);

        var memberships = db.GroupMemberships.Where(m => m.LeftOn == null);
        memberships = r.GroupId is { } gid ? memberships.Where(m => m.GroupId == gid)
            : me.IsAdmin() ? memberships : memberships.Where(m => me.LedGroupIds.Contains(m.GroupId));
        // shortcut: one page of 500 people; page by name if a group (or an admin's whole church) grows past that.
        var people = await memberships
            .Where(m => m.UserId != me.AppUserId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.FirstName, u.LastName, u.Nickname, m.GroupId })
            .Join(db.Groups, x => x.GroupId, gr => gr.Id, (x, gr) => new { x.Id, x.FirstName, x.LastName, x.Nickname, Group = gr.Name })
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Take(500)
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var devotions = await db.Devotions.AsNoTracking().Where(d => d.DevotionDate == date && ids.Contains(d.UserId)).ToListAsync(ct);

        DevotionBriefDto? Brief(Guid userId, DevotionSlot slot) => devotions.Where(d => d.UserId == userId && d.Slot == slot).Select(d =>
        {
            var score = DevotionAccess.Max(d.AiTextScore, d.AiImageScore);
            return new DevotionBriefDto(d.Id, d.AiStatus, score, settings.Band(score), d.ReviewedAt is not null, d.ImagePath is not null);
        }).FirstOrDefault();

        var rows = people.Select(p => new MemberDevotionsDto(p.Id, Events.EventAccess.Display(p.FirstName, p.LastName, p.Nickname), p.Group,
            Brief(p.Id, DevotionSlot.DAY), Brief(p.Id, DevotionSlot.NIGHT))).ToList();
        var submitted = rows.Count(x => x.Day is not null || x.Night is not null);
        if (r.LikelyAiOnly) rows = rows.Where(x => x.Day?.Band == "LIKELY_AI" || x.Night?.Band == "LIKELY_AI").ToList();
        return new(date, rows, submitted, people.Count);
    }
}

// ---------------------------------------------------------------- Review (§6.3 fairness rule)

/// <summary>Marks a devotion as looked at by a leader. It changes nothing else: scores are advisory only.</summary>
public sealed record ReviewDevotion(Guid Id, string? Note) : IRequest<Guid>, IAuditedRequest
{
    public string AuditAction => "devotion.review";
}

public sealed class ReviewDevotionValidator : AbstractValidator<ReviewDevotion>
{
    public ReviewDevotionValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public sealed class ReviewDevotionHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<ReviewDevotion, Guid>
{
    public async Task<Guid> Handle(ReviewDevotion r, CancellationToken ct)
    {
        var me = current.Info;
        var d = await db.Devotions.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException();
        if (d.UserId == me.AppUserId || !await DevotionAccess.Reviewable(db, me).AnyAsync(id => id == d.UserId, ct)) throw new NotFoundException();
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        var before = new { reviewed = d.ReviewedAt is not null, note = d.ReviewNote is not null };
        d.Review(me.AppUserId, note, clock.GetUtcNow());
        audit.Describe("devotion", d.Id, before, new { reviewed = true, note = note is not null });
        return d.Id;
    }
}
