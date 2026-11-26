using TimeCheck.Domain.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Domain.Entities;

public sealed class AppUser : BaseEntity, IChurchScoped, IAuditedEntity
{
    public const int AdultAge = 18;

    /// <summary>Personal settings as JSON (app_users.settings), e.g. notification channels per category (§6.7).</summary>
    public string Settings { get; private set; } = "{}";

    public void ReplaceSettings(string json) => Settings = json;

    private AppUser() { }

    public Guid ChurchId { get; private set; }
    public Guid? AuthUserId { get; private set; }
    public UserRole Role { get; private set; } = UserRole.MEMBER;
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string? Nickname { get; private set; }
    public Gender Gender { get; private set; }
    /// <summary>Encrypted at rest (§8.4).</summary>
    public DateOnly Birthdate { get; private set; }
    public string? GradeLevel { get; private set; }
    /// <summary>Encrypted at rest.</summary>
    public string? Mobile { get; private set; }
    public string Email { get; private set; } = "";
    /// <summary>Encrypted at rest.</summary>
    public string? Address { get; private set; }
    public MemberStatus Status { get; private set; } = MemberStatus.NEW_MEMBER;
    public DateOnly JoinedOn { get; private set; }
    public string? PhotoPath { get; private set; }
    public bool IsMinor { get; private set; }
    public string? GuardianName { get; private set; }
    /// <summary>Encrypted at rest.</summary>
    public string? GuardianContact { get; private set; }
    public DateTimeOffset? GuardianConsentAt { get; private set; }
    public string? PrivacyConsentVersion { get; private set; }
    public DateTimeOffset? PrivacyConsentAt { get; private set; }
    public bool MustChangePassword { get; private set; } = true;
    public DateTimeOffset? DeactivatedAt { get; private set; }
    /// <summary>Access tokens issued before this are refused (§6.10 force logout).</summary>
    public DateTimeOffset? SessionsRevokedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? $"{FirstName} {LastName}" : Nickname!;
    public bool IsActive => DeactivatedAt is null && DeletedAt is null;

    public sealed record Guardian(string Name, string Contact, DateTimeOffset ConsentAt);

    public static AppUser Create(
        Guid churchId, string firstName, string lastName, Gender gender, DateOnly birthdate, string email,
        DateOnly today, DateOnly? joinedOn = null, string? nickname = null, string? gradeLevel = null,
        string? mobile = null, string? address = null, Guardian? guardian = null,
        MemberStatus status = MemberStatus.NEW_MEMBER, Guid? createdBy = null)
    {
        if (birthdate > today) throw new DomainException("birthdate.future", "Birthdate cannot be in the future.");

        var user = new AppUser
        {
            ChurchId = churchId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Nickname = Blank(nickname),
            Gender = gender,
            Birthdate = birthdate,
            Email = email.Trim(),
            GradeLevel = Blank(gradeLevel),
            Mobile = Blank(mobile),
            Address = Blank(address),
            Status = status,
            JoinedOn = joinedOn ?? today,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
        };
        user.SetGuardian(guardian, today);
        return user;
    }

    public static int AgeOn(DateOnly birthdate, DateOnly today)
    {
        var age = today.Year - birthdate.Year;
        return birthdate.AddYears(age) > today ? age - 1 : age;
    }

    /// <summary>Recomputes the minor flag; a minor must always have guardian consent (§6.4, §8.4).</summary>
    public void SetGuardian(Guardian? guardian, DateOnly today)
    {
        IsMinor = AgeOn(Birthdate, today) < AdultAge;
        if (IsMinor && guardian is null)
            throw new DomainException("guardian.required", "Members under 18 need a guardian and guardian consent.");

        GuardianName = guardian?.Name.Trim();
        GuardianContact = guardian?.Contact.Trim();
        GuardianConsentAt = guardian?.ConsentAt;
    }

    /// <summary>Daily job: someone who turned 18 stops being a minor.</summary>
    public bool RefreshMinorFlag(DateOnly today)
    {
        var minor = AgeOn(Birthdate, today) < AdultAge;
        if (minor == IsMinor) return false;
        IsMinor = minor;
        return true;
    }

    public void LinkAuthUser(Guid authUserId) => AuthUserId = authUserId;

    /// <summary>A new church's first Super Admin, from the one-time setup command. Every later change goes through ChangeRole.</summary>
    public void MakeFirstSuperAdmin() => Role = UserRole.SUPER_ADMIN;

    public RoleChange ChangeRole(UserRole newRole, AppUser actor, string? reason)
    {
        if (newRole == Role) throw new DomainException("role.unchanged", "User already has this role.");
        var touchesAdmin = newRole.IsAtLeast(UserRole.ADMIN) || Role.IsAtLeast(UserRole.ADMIN);
        var required = touchesAdmin ? UserRole.SUPER_ADMIN : UserRole.ADMIN;
        if (!actor.Role.IsAtLeast(required))
            throw new DomainException("role.forbidden", "You are not allowed to make this role change.");
        if (actor.Id == Id) throw new DomainException("role.self", "You cannot change your own role.");

        var change = RoleChange.Record(ChurchId, Id, Role, newRole, actor.Id, reason);
        Role = newRole;
        UpdatedBy = actor.Id;
        return change;
    }

    public MemberStatusChange ChangeStatus(MemberStatus newStatus, Guid actorId, string? reason)
    {
        if (newStatus == Status) throw new DomainException("status.unchanged", "Member already has this status.");
        var change = MemberStatusChange.Record(ChurchId, Id, Status, newStatus, actorId, reason);
        Status = newStatus;
        UpdatedBy = actorId;
        return change;
    }

    /// <summary>Fields only the member's leader or an admin may change, to keep reports trustworthy (§6.9).</summary>
    public void UpdateDetails(string firstName, string lastName, string? nickname, Gender gender, DateOnly birthdate,
        string? gradeLevel, string? mobile, string? address, DateOnly joinedOn, Guardian? guardian, DateOnly today, Guid actorId)
    {
        if (birthdate > today) throw new DomainException("birthdate.future", "Birthdate cannot be in the future.");
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Nickname = Blank(nickname);
        Gender = gender;
        Birthdate = birthdate;
        GradeLevel = Blank(gradeLevel);
        Mobile = Blank(mobile);
        Address = Blank(address);
        JoinedOn = joinedOn;
        SetGuardian(guardian, today);
        UpdatedBy = actorId;
    }

    /// <summary>Fields a member may edit themselves (§6.9).</summary>
    public void UpdateOwnProfile(string? nickname, string? mobile, string? address)
    {
        Nickname = Blank(nickname);
        Mobile = Blank(mobile);
        Address = Blank(address);
        UpdatedBy = Id;
    }

    public void RecordPrivacyConsent(string version, DateTimeOffset at)
    {
        PrivacyConsentVersion = version;
        PrivacyConsentAt = at;
    }

    public void PasswordChanged() => MustChangePassword = false;

    public void RevokeSessions(DateTimeOffset at, Guid actorId)
    {
        SessionsRevokedAt = at;
        UpdatedBy = actorId;
    }

    public void Deactivate(DateTimeOffset at, Guid actorId)
    {
        DeactivatedAt ??= at;
        UpdatedBy = actorId;
    }

    public void Reactivate(Guid actorId)
    {
        DeactivatedAt = null;
        UpdatedBy = actorId;
    }

    public const string ErasedFirstName = "Removed";
    public const string ErasedLastName = "member";

    /// <summary>
    /// Data-subject erasure (§8.4): personal details go, the row stays so attendance counts and history still add up.
    /// Only the birth year and gender are kept, for age and gender aggregates. Only a deactivated person can be erased.
    /// </summary>
    public void Erase(DateTimeOffset at, Guid? actorId)
    {
        if (DeletedAt is not null) throw new DomainException("member.already_erased", "This person has already been erased.");
        if (DeactivatedAt is null) throw new DomainException("member.erase_active", "Deactivate them first, then erase their data.");
        FirstName = ErasedFirstName;
        LastName = ErasedLastName;
        Nickname = GradeLevel = Mobile = Address = PhotoPath = GuardianName = GuardianContact = null;
        GuardianConsentAt = null;
        IsMinor = false;
        Birthdate = new DateOnly(Birthdate.Year, 1, 1);
        Email = $"erased-{Id:N}@erased.invalid";
        Settings = "{}";
        AuthUserId = null;
        Role = UserRole.MEMBER;
        DeletedAt = at;
        UpdatedBy = actorId;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
