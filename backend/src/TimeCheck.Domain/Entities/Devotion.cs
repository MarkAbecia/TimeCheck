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
