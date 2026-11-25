using Microsoft.EntityFrameworkCore;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Abstractions;

/// <summary>The signed-in user, resolved from app_users (never from token claims). See ADR 0002.</summary>
public sealed record CurrentUserInfo(
    Guid AppUserId, Guid ChurchId, UserRole Role, bool IsActive, IReadOnlyList<Guid> LedGroupIds, Guid? GroupId,
    bool MustChangePassword = false, string? PrivacyConsentVersion = null, DateTimeOffset? SessionsRevokedAt = null);

/// <summary>Account rules set by configuration (Api layer).</summary>
public interface IAccountPolicy
{
    /// <summary>The privacy notice version users must accept before using the app (§8.4).</summary>
    string PrivacyConsentVersion { get; }
    bool RequireAdminMfa { get; }
}

/// <summary>Supabase Auth admin operations; the service role key lives only in the backend (§8.1).</summary>
public interface IAuthAdmin
{
    /// <summary>Throws <see cref="FluentValidation.ValidationException"/> when Supabase rejects the password.</summary>
    Task SetPasswordAsync(Guid authUserId, string newPassword, CancellationToken ct);

    /// <summary>Creates the sign-in account and emails an invite link (§6.4). Throws ConflictException if the email is taken.</summary>
    Task<Guid> InviteUserAsync(string email, CancellationToken ct);

    /// <summary>Removes a sign-in account; used to undo an invite when saving the member fails.</summary>
    Task DeleteUserAsync(Guid authUserId, CancellationToken ct);

    /// <summary>Removes every MFA factor (§6.10 reset MFA), so the next sign-in sets up a new code. Returns how many were removed.</summary>
    Task<int> RemoveMfaFactorsAsync(Guid authUserId, CancellationToken ct);
}

/// <summary>Shadow columns holding blind indexes on app_users, for dedup queries via EF.Property.</summary>
public static class BlindIndexColumns
{
    public const string Mobile = "MobileBidx";
    public const string NameBirthdate = "NameBirthdateBidx";
}

public static class Onboarding
{
    /// <summary>A user may use the app only after the forced password change and the current privacy consent (§6.1, §8.4).</summary>
    public static bool IsComplete(this CurrentUserInfo u, IAccountPolicy policy) =>
        !u.MustChangePassword && u.PrivacyConsentVersion == policy.PrivacyConsentVersion;
}

public interface ICurrentUser
{
    /// <summary>Throws if there is no authenticated user (should never happen behind the fallback policy).</summary>
    CurrentUserInfo Info { get; }
}

/// <summary>Looks up (and caches) the app user behind a Supabase auth user id.</summary>
public interface IUserDirectory
{
    Task<CurrentUserInfo?> FindByAuthUserIdAsync(Guid authUserId, CancellationToken ct);
    Task InvalidateAsync(Guid authUserId, CancellationToken ct);
}

/// <summary>Request metadata for audit rows.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

/// <summary>HMAC blind index for searching/deduplicating encrypted columns (§8.4).</summary>
public interface IBlindIndexer
{
    string Mobile(string mobile);
    string NameBirthdate(string firstName, string lastName, DateOnly birthdate);
    string Ip(string ip);
}

public interface IAppDbContext
{
    DbSet<Church> Churches { get; }
    DbSet<AppUser> Users { get; }
    DbSet<CellGroup> Groups { get; }
    DbSet<GroupMembership> GroupMemberships { get; }
    DbSet<EventType> EventTypes { get; }
    DbSet<Event> Events { get; }
    DbSet<EventAudience> EventAudiences { get; }
    DbSet<Attendance> Attendance { get; }
    DbSet<Devotion> Devotions { get; }
    DbSet<Invite> Invites { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<RoleChange> RoleChanges { get; }
    DbSet<MemberStatusChange> MemberStatusChanges { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ReportJob> ReportJobs { get; }
    DbSet<AttendanceFact> AttendanceFacts { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Retention (§8.4): deletes one church's audit rows older than <paramref name="before"/>; the database refuses anything under a year.</summary>
    Task<int> PurgeAuditLogsAsync(Guid churchId, DateTimeOffset before, CancellationToken ct);
    /// <summary>Deletes a person's Supabase Auth sessions (and so their refresh tokens), in the current transaction.</summary>
    Task<int> RevokeAuthSessionsAsync(Guid authUserId, CancellationToken ct);

    /// <summary>Forgets tracked changes after a failed save, so a long-running job can carry on with the next item.</summary>
    void DiscardChanges();
}

/// <summary>
/// Report results per church (§9 caching): kept until the church's next audited write, or 10 minutes. Callers asking for the
/// same report at once share one build. A cache outage only makes reports slower.
/// </summary>
public interface IReportCache
{
    Task<byte[]> GetOrCreateAsync(Guid churchId, string key, Func<CancellationToken, Task<byte[]>> build, CancellationToken ct);
    Task InvalidateAsync(Guid churchId);
}

/// <summary>Private object storage (Supabase Storage). Clients only ever get short-lived signed URLs (§6.3, §8.3).</summary>
public interface IFileStorage
{
    Task UploadAsync(string bucket, string path, byte[] content, string contentType, CancellationToken ct);
    Task<byte[]> DownloadAsync(string bucket, string path, CancellationToken ct);
    Task<string> SignedUrlAsync(string bucket, string path, TimeSpan ttl, CancellationToken ct);
    Task DeleteAsync(string bucket, string path, CancellationToken ct);
}

public sealed record CleanImage(byte[] Bytes, string ContentType);

/// <summary>
/// Checks an upload by its magic bytes and re-encodes it, which drops EXIF/GPS and anything smuggled in the file (§8.3).
/// Throws <see cref="Domain.Common.DomainException"/> "upload.image_invalid" for anything that isn't a JPEG, PNG or WebP.
/// </summary>
public interface IImageSanitizer
{
    CleanImage Sanitize(byte[] input);
}

/// <summary>0–100: how likely the content is AI-generated, and who said so.</summary>
public sealed record AiScore(decimal Score, string Provider, string Model);

/// <summary>An AI-content detection provider (§6.3, ADR 0006). Returns null for content it doesn't handle.</summary>
public interface IAiContentDetector
{
    /// <summary>False when detection is switched off (no provider configured): devotions are marked SKIPPED.</summary>
    bool Enabled { get; }
    Task<AiScore?> ScoreTextAsync(string text, CancellationToken ct);
    Task<AiScore?> ScoreImageAsync(byte[] image, string contentType, CancellationToken ct);
}

/// <summary>Sends one email (§6.7). Which provider is an owner decision (ADR 0007); "none" leaves notifications in-app.</summary>
public interface IEmailSender
{
    bool Enabled { get; }
    /// <summary>The web app's address, for links in emails.</summary>
    string AppUrl { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>Web push with VAPID (§6.7). <see cref="PublicKey"/> is what browsers subscribe with.</summary>
public interface IPushSender
{
    bool Enabled { get; }
    string? PublicKey { get; }
    Task<PushOutcome> SendAsync(string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct);
}

/// <summary>Gone: the browser unsubscribed or the subscription expired, so it's removed.</summary>
public enum PushOutcome { Sent, Gone, Failed }
