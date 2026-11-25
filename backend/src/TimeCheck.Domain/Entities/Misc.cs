using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class Invite : BaseEntity, IChurchScoped
{
    private Invite() { }
    public Guid ChurchId { get; private set; }
    public Guid InviterId { get; private set; }
    public Guid EventId { get; private set; }
    public string GuestName { get; private set; } = "";
    /// <summary>Encrypted at rest.</summary>
    public string? GuestContact { get; private set; }
    public InviteStatus Status { get; private set; } = InviteStatus.EXPECTED;
    public Guid? ConvertedUserId { get; private set; }
    public DateOnly PurgeAfter { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public const int MaxNameLength = 200;
    public const int MaxContactLength = 200;
    public const string ErasedName = "Removed guest";

    /// <summary>A member logs someone they invited (§6.5). Guest details go only as far as <paramref name="purgeAfter"/>.</summary>
    public static Invite Log(Guid churchId, Guid inviterId, Guid eventId, string guestName, string? guestContact, DateOnly purgeAfter)
    {
        var i = new Invite { ChurchId = churchId, InviterId = inviterId, EventId = eventId, PurgeAfter = purgeAfter };
        i.Edit(guestName, guestContact);
        return i;
    }

    public void Edit(string guestName, string? guestContact)
    {
        ThrowIfConverted();
        guestName = guestName.Trim();
        guestContact = string.IsNullOrWhiteSpace(guestContact) ? null : guestContact.Trim();
        if (guestName.Length is 0 or > MaxNameLength) throw new DomainException("invite.name", "Enter the guest's name.");
        if (guestContact is { Length: > MaxContactLength }) throw new DomainException("invite.contact", "The contact is too long.");
        GuestName = guestName;
        GuestContact = guestContact;
    }

    /// <summary>EXPECTED → CAME or DID_NOT_COME, and back while it isn't converted.</summary>
    public void Mark(InviteStatus status)
    {
        if (status == Status) return;
        ThrowIfConverted();
        Status = status;
    }

    /// <summary>The leader registered the guest as a member; the invite now counts as converted.</summary>
    public void Convert(Guid userId)
    {
        ThrowIfConverted();
        if (Status != InviteStatus.CAME) throw new DomainException("invite.not_came", "Only a guest who came can be registered.");
        ConvertedUserId = userId;
    }

    /// <summary>Removes the guest's details and hides the invite: on request, or by the retention job (§8.4).</summary>
    public void Erase(DateTimeOffset now)
    {
        ThrowIfConverted();
        GuestName = ErasedName;
        GuestContact = null;
        DeletedAt = now;
    }

    private void ThrowIfConverted()
    {
        if (ConvertedUserId is not null) throw new DomainException("invite.converted", "This guest is already a member.");
    }
}

/// <summary>Outbox row: written in the same transaction as the change; delivered by the Worker.</summary>
public sealed class Notification : BaseEntity, IChurchScoped
{
    private Notification() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = "";
    public string Payload { get; private set; } = "{}";
    public IReadOnlyList<string> Channels { get; private set; } = ["in_app"];
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public static Notification Create(Guid churchId, Guid userId, string type, string payloadJson, IReadOnlyList<string> channels, DateTimeOffset now) => new()
    {
        ChurchId = churchId,
        UserId = userId,
        Type = type,
        Payload = payloadJson,
        Channels = channels,
        NextAttemptAt = now,
    };

    public const int MaxAttempts = 6;

    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;

    /// <summary>Taken by a delivery run: others skip it until <paramref name="until"/>, so a slow run isn't sent twice.</summary>
    public void Claim(DateTimeOffset until) => NextAttemptAt = until;

    /// <summary>Delivered (or nothing to send): <paramref name="channels"/> records where it actually went.</summary>
    public void Delivered(IReadOnlyList<string> channels, DateTimeOffset at)
    {
        Channels = channels;
        DeliveredAt = at;
        LastError = null;
    }

    /// <summary>Retried with backoff (2, 4, 8 … minutes); after <see cref="MaxAttempts"/> it stays in-app only.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts) Delivered(["in_app"], now);
        else NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts));
    }
}

/// <summary>A browser someone allowed notifications on (§6.7). The keys are the browser's public ones, used to encrypt pushes.</summary>
public sealed class PushSubscription : BaseEntity, IChurchScoped
{
    private PushSubscription() { }
    public Guid ChurchId { get; private set; }
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; } = "";
    public string P256dh { get; private set; } = "";
    public string Auth { get; private set; } = "";

    public static PushSubscription Create(Guid churchId, Guid userId, string endpoint, string p256dh, string auth) =>
        new() { ChurchId = churchId, UserId = userId, Endpoint = endpoint, P256dh = p256dh, Auth = auth };

    /// <summary>The same browser, signed in as someone else now, or with fresh keys.</summary>
    public void Reassign(Guid userId, string p256dh, string auth)
    {
        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
    }
}

public sealed class ReportJob : BaseEntity, IChurchScoped
{
    private ReportJob() { }
    public Guid ChurchId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Type { get; private set; } = "";
    public string Params { get; private set; } = "{}";
    public ReportJobStatus Status { get; private set; } = ReportJobStatus.QUEUED;
    public string? FilePath { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Export files are kept this long; the download link never outlives the file (§6.8).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static ReportJob Queue(Guid churchId, Guid requestedBy, string type, string paramsJson) => new()
    {
        ChurchId = churchId,
        RequestedBy = requestedBy,
        Type = type,
        Params = paramsJson,
    };

    public void Start() => Status = ReportJobStatus.RUNNING;

    public void Complete(string filePath, DateTimeOffset now)
    {
        Status = ReportJobStatus.DONE;
        FilePath = filePath;
        Error = null;
        CompletedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = ReportJobStatus.FAILED;
        Error = error.Length > 500 ? error[..500] : error;
        CompletedAt = now;
    }

    public void Expire()
    {
        Status = ReportJobStatus.EXPIRED;
        FilePath = null;
    }
}

/// <summary>
/// One person expected at (or marked at) one started event, with how it went: the attendance_facts view every report counts
/// from. Read-only.
/// </summary>
public sealed class AttendanceFact : IChurchScoped
{
    private AttendanceFact() { }
    public Guid ChurchId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventTypeId { get; private set; }
    public DateOnly OccursOn { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? GroupId { get; private set; }
    public AttendanceStatus Status { get; private set; }
}
