using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class Devotion : BaseEntity, IChurchScoped
{
    public const int MaxTextLength = 5000;

    private Devotion() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly DevotionDate { get; private set; }
    public DevotionSlot Slot { get; private set; }
    public string? TextContent { get; private set; }
    public string? ImagePath { get; private set; }
    public string ContentHash { get; private set; } = "";
    public AiStatus AiStatus { get; private set; } = AiStatus.PENDING;
    public decimal? AiTextScore { get; private set; }
    public decimal? AiImageScore { get; private set; }
    public string? AiProvider { get; private set; }
    public string? AiModelVersion { get; private set; }
    public DateTimeOffset? AiCheckedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }

    public static Devotion Submit(Guid churchId, Guid userId, DateOnly date, DevotionSlot slot,
        string? text, string? imagePath, string contentHash, DateTimeOffset now)
    {
        var d = new Devotion { ChurchId = churchId, UserId = userId, DevotionDate = date, Slot = slot };
        d.Replace(text, imagePath, contentHash, now);
        return d;
    }

    /// <summary>A second upload in the same slot edits the first (§6.3) and re-runs detection if content changed.</summary>
    public void Replace(string? text, string? imagePath, string contentHash, DateTimeOffset now)
    {
        text = string.IsNullOrWhiteSpace(text) ? null : text;
        if (text is null && imagePath is null) throw new DomainException("devotion.empty", "Add text or an image.");
        if (text is { Length: > MaxTextLength }) throw new DomainException("devotion.too_long", "Devotion text is too long.");

        var changed = contentHash != ContentHash;
        TextContent = text;
        ImagePath = imagePath;
        ContentHash = contentHash;
        SubmittedAt = now;
        if (changed)
        {
            AiStatus = AiStatus.PENDING;
            AiTextScore = AiImageScore = null;
            AiCheckedAt = null;
        }
    }

    public void RecordAiResult(decimal? textScore, decimal? imageScore, string provider, string modelVersion, DateTimeOffset at)
    {
        AiTextScore = textScore;
        AiImageScore = imageScore;
        AiProvider = provider;
        AiModelVersion = modelVersion;
        AiCheckedAt = at;
        AiStatus = AiStatus.DONE;
    }

    public void MarkAiFailed() => AiStatus = AiStatus.FAILED;
    public void MarkAiSkipped() => AiStatus = AiStatus.SKIPPED;

    public void Review(Guid reviewerId, string? note, DateTimeOffset at)
    {
        ReviewedBy = reviewerId;
        ReviewedAt = at;
        ReviewNote = note;
    }
}

/// <summary>
/// When each slot accepts uploads, in church wall time (§6.3, §15 Q4). A slot is open from its opening minute through its
/// closing minute; a closing time at or before the opening time means the next day (Night: 18:00 to 05:00).
/// </summary>
public sealed class DevotionWindows(TimeOnly dayOpens, TimeOnly dayCloses, TimeOnly nightOpens, TimeOnly nightCloses)
{
    public TimeOnly DayOpens { get; } = dayOpens;
    public TimeOnly DayCloses { get; } = dayCloses;
    public TimeOnly NightOpens { get; } = nightOpens;
    public TimeOnly NightCloses { get; } = nightCloses;

    public static readonly DevotionWindows Default = new(new(0, 0), new(23, 59), new(18, 0), new(5, 0));

    /// <summary>[Opens, Closes) for the slot that belongs to <paramref name="date"/>.</summary>
    public (DateTime Opens, DateTime Closes) Window(DevotionSlot slot, DateOnly date)
    {
        var (opens, closes) = slot == DevotionSlot.DAY ? (DayOpens, DayCloses) : (NightOpens, NightCloses);
        var start = date.ToDateTime(opens);
        var end = date.AddDays(closes <= opens ? 1 : 0).ToDateTime(closes).AddMinutes(1);
        return (start, end);
    }

    public bool IsOpen(DevotionSlot slot, DateOnly date, DateTime localNow)
    {
        var (opens, closes) = Window(slot, date);
        return localNow >= opens && localNow < closes;
    }

    /// <summary>The slots open right now: today's, plus last night's until it closes in the morning.</summary>
    public IEnumerable<(DevotionSlot Slot, DateOnly Date)> OpenAt(DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        foreach (var date in new[] { today.AddDays(-1), today })
            foreach (var slot in new[] { DevotionSlot.DAY, DevotionSlot.NIGHT })
                if (IsOpen(slot, date, localNow)) yield return (slot, date);
    }
}
