using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Members;

public sealed record GuardianInput(string Name, string Contact, bool Consent);

/// <summary>A member's full record, for their leader and admins only (§4.2, §8.4: minors' data included).</summary>
public sealed record MemberDto(
    Guid Id, string FirstName, string LastName, string? Nickname, string DisplayName, string Email, Gender Gender,
    DateOnly Birthdate, string? GradeLevel, string? Mobile, string? Address, MemberStatus Status, UserRole Role,
    DateOnly JoinedOn, bool IsMinor, string? GuardianName, string? GuardianContact, DateTimeOffset? GuardianConsentAt,
    Guid? GroupId, string? GroupName, bool IsActive);

internal static class Members
{
    public const string MobilePattern = @"^\+?[0-9 ()-]{7,20}$";

    /// <summary>
    /// The target user if the caller may manage them: admins anyone in the church, leaders the members of a group they
    /// lead. Only a super admin manages admins. Everything else is 404 so ids can't be probed (§8.1).
    /// </summary>
    public static async Task<(AppUser User, Guid? GroupId)> LoadManageable(
        IAppDbContext db, CurrentUserInfo me, Guid id, bool tracking, CancellationToken ct)
    {
        if (id == me.AppUserId) throw new ForbiddenException("Change your own details from your profile.");
        var user = await (tracking ? db.Users : db.Users.AsNoTracking()).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException();
        var groupId = await ActiveGroupId(db, id, ct);
        var inReach = me.IsAdmin() || (groupId is { } g && me.LedGroupIds.Contains(g));
        var outranked = !user.Role.IsAtLeast(UserRole.ADMIN) || me.Role == UserRole.SUPER_ADMIN;
        if (!inReach || !outranked) throw new NotFoundException();
        return (user, groupId);
    }

    public static Task<Guid?> ActiveGroupId(IAppDbContext db, Guid userId, CancellationToken ct) =>
        db.GroupMemberships.Where(m => m.UserId == userId && m.LeftOn == null).Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);

    public static async Task<MemberDto> ToDto(IAppDbContext db, AppUser u, Guid? groupId, CancellationToken ct)
    {
        var groupName = groupId is null ? null : await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        return new MemberDto(u.Id, u.FirstName, u.LastName, u.Nickname, u.DisplayName, u.Email, u.Gender, u.Birthdate, u.GradeLevel,
            u.Mobile, u.Address, u.Status, u.Role, u.JoinedOn, u.IsMinor, u.GuardianName, u.GuardianContact, u.GuardianConsentAt,
            groupId, groupName, u.IsActive);
    }

    /// <summary>Today in the church's timezone (birthdays, join dates and membership dates are local dates).</summary>
    public static async Task<DateOnly> Today(IAppDbContext db, Guid churchId, TimeProvider clock, CancellationToken ct)
    {
        var tz = await db.Churches.AsNoTracking().Where(c => c.Id == churchId).Select(c => c.Timezone).FirstAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(tz)).DateTime);
    }

    /// <summary>A guardian is kept only for minors; an unchanged guardian keeps its original consent date.</summary>
    public static AppUser.Guardian? Guardian(GuardianInput? input, DateOnly birthdate, DateOnly today, DateTimeOffset now, AppUser? existing = null)
    {
        if (input is null || AppUser.AgeOn(birthdate, today) >= AppUser.AdultAge) return null;
        var same = existing is { GuardianConsentAt: { } at } && existing.GuardianName == input.Name.Trim() && existing.GuardianContact == input.Contact.Trim();
        return new AppUser.Guardian(input.Name, input.Contact, same ? existing!.GuardianConsentAt!.Value : now);
    }

    public static void EvictCache(UnitOfWork uow, IUserDirectory users, Guid? authUserId)
    {
        if (authUserId is { } id) uow.AfterCommit(() => users.InvalidateAsync(id, CancellationToken.None));
    }
}

/// <summary>Shared field rules for registering and editing a member (§6.4).</summary>
internal static class MemberRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string> first, Func<T, string> last, Func<T, string?> nickname,
        Func<T, Gender> gender, Func<T, DateOnly> birthdate, Func<T, string?> grade, Func<T, string?> mobile, Func<T, string?> address,
        Func<T, GuardianInput?> guardian, TimeProvider clock)
    {
        v.RuleFor(x => first(x)).NotEmpty().MaximumLength(100).OverridePropertyName("firstName");
        v.RuleFor(x => last(x)).NotEmpty().MaximumLength(100).OverridePropertyName("lastName");
        v.RuleFor(x => nickname(x)).MaximumLength(100).OverridePropertyName("nickname");
        v.RuleFor(x => gender(x)).IsInEnum().OverridePropertyName("gender");
        v.RuleFor(x => birthdate(x))
            .Must(d => d <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(1)) && d.Year >= clock.GetUtcNow().Year - 120)
            .WithMessage("Enter a real birthdate.").OverridePropertyName("birthdate");
        v.RuleFor(x => grade(x)).MaximumLength(50).OverridePropertyName("gradeLevel");
        v.RuleFor(x => mobile(x)).Matches(Members.MobilePattern).When(x => !string.IsNullOrWhiteSpace(mobile(x)))
            .WithMessage("Enter a mobile number like 0917 123 4567.").OverridePropertyName("mobile");
        v.RuleFor(x => address(x)).MaximumLength(500).OverridePropertyName("address");
        v.RuleFor(x => guardian(x)!.Name).NotEmpty().MaximumLength(200).When(x => guardian(x) is not null).OverridePropertyName("guardian.name");
        v.RuleFor(x => guardian(x)!.Contact).NotEmpty().MaximumLength(100).When(x => guardian(x) is not null).OverridePropertyName("guardian.contact");
        v.RuleFor(x => guardian(x)!.Consent).Equal(true).When(x => guardian(x) is not null)
            .WithMessage("The guardian must agree before a minor is registered.").OverridePropertyName("guardian.consent");
    }
}

// ---------------------------------------------------------------- Register (§6.4)

public sealed record RegisterMember(
    Guid GroupId, string FirstName, string LastName, string? Nickname, Gender Gender, DateOnly Birthdate, string? GradeLevel,
    string Mobile, string Email, string? Address, DateOnly? JoinedOn, MemberStatus? Status, GuardianInput? Guardian,
    bool ConfirmDuplicates = false, Guid? InviteId = null) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => "member.register";
}

public sealed class RegisterMemberValidator : AbstractValidator<RegisterMember>
{
    public RegisterMemberValidator(TimeProvider clock)
    {
        MemberRules.Apply(this, x => x.FirstName, x => x.LastName, x => x.Nickname, x => x.Gender, x => x.Birthdate, x => x.GradeLevel,
            x => x.Mobile, x => x.Address, x => x.Guardian, clock);
        RuleFor(x => x.Mobile).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.JoinedOn).Must(d => d is null || d <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(1)))
            .WithMessage("The join date can't be in the future.");
    }
}

public sealed class RegisterMemberHandler(
    IAppDbContext db, ICurrentUser current, IBlindIndexer index, IAuthAdmin authAdmin, UnitOfWork uow, AuditScope audit, TimeProvider clock)
    : IRequestHandler<RegisterMember, MemberDto>
{
    public async Task<MemberDto> Handle(RegisterMember r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.CanManageGroup(r.GroupId)) throw new NotFoundException();
        if (!await db.Groups.AnyAsync(g => g.Id == r.GroupId && g.ArchivedAt == null, ct)) throw new NotFoundException();

        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new ConflictException("A member with this email already exists.");

        // Registering a guest who came (§6.5): the invite must be one this leader reaches, and is linked to the new member.
        var invite = r.InviteId is { } inviteId ? await Features.Invites.InviteAccess.Load(db, me, inviteId, ct) : null;

        // Possible duplicates (§6.4): warn, and let the leader confirm. Matches by blind index; nobody's details are returned.
        if (!r.ConfirmDuplicates)
        {
            var mobileIdx = index.Mobile(r.Mobile.Trim());
            var nameIdx = index.NameBirthdate(r.FirstName.Trim(), r.LastName.Trim(), r.Birthdate);
            var hits = await db.Users.AsNoTracking()
                .Where(u => EF.Property<string?>(u, BlindIndexColumns.Mobile) == mobileIdx || EF.Property<string>(u, BlindIndexColumns.NameBirthdate) == nameIdx)
                .Select(u => new
                {
                    Mobile = EF.Property<string?>(u, BlindIndexColumns.Mobile) == mobileIdx,
                    SameGroup = db.GroupMemberships.Any(m => m.UserId == u.Id && m.LeftOn == null && m.GroupId == r.GroupId),
                })
                .ToListAsync(ct);
            if (hits.Count > 0)
                throw new DuplicateWarningException(hits.Select(h => new DuplicateMatch(h.Mobile ? "mobile" : "nameBirthdate", h.SameGroup)).Distinct().ToList());
        }

        var today = await Members.Today(db, me.ChurchId, clock, ct);
        var user = AppUser.Create(me.ChurchId, r.FirstName, r.LastName, r.Gender, r.Birthdate, email, today, r.JoinedOn, r.Nickname,
            r.GradeLevel, r.Mobile, r.Address, Members.Guardian(r.Guardian, r.Birthdate, today, clock.GetUtcNow()),
            r.Status ?? MemberStatus.NEW_MEMBER, me.AppUserId);

        // The invite is outside the transaction: if saving fails, the new sign-in account is removed again.
        var authUserId = await authAdmin.InviteUserAsync(email, ct);
        uow.OnRollback(() => authAdmin.DeleteUserAsync(authUserId, CancellationToken.None));
        user.LinkAuthUser(authUserId);

        db.Users.Add(user);
        db.GroupMemberships.Add(GroupMembership.Start(me.ChurchId, r.GroupId, user.Id, user.JoinedOn));
        invite?.Convert(user.Id);
        audit.Describe("app_user", user.Id, null,
            new { groupId = r.GroupId, status = user.Status, user.IsMinor, duplicatesConfirmed = r.ConfirmDuplicates, fromInvite = r.InviteId });
        return await Members.ToDto(db, user, r.GroupId, ct);
    }
}

// ---------------------------------------------------------------- View and edit (§6.9)

public sealed record GetMember(Guid Id) : IRequest<MemberDto>;

public sealed class GetMemberHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<GetMember, MemberDto>
{
    public async Task<MemberDto> Handle(GetMember r, CancellationToken ct)
    {
        var (user, groupId) = await Members.LoadManageable(db, current.Info, r.Id, tracking: false, ct);
        return await Members.ToDto(db, user, groupId, ct);
    }
}

/// <summary>Leader/admin edit. Email isn't here: changing it needs re-verification and arrives with account management.</summary>
public sealed record UpdateMember(
    Guid Id, string FirstName, string LastName, string? Nickname, Gender Gender, DateOnly Birthdate, string? GradeLevel,
    string? Mobile, string? Address, DateOnly JoinedOn, GuardianInput? Guardian) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => "member.update";
}

public sealed class UpdateMemberValidator : AbstractValidator<UpdateMember>
{
    public UpdateMemberValidator(TimeProvider clock) =>
        MemberRules.Apply(this, x => x.FirstName, x => x.LastName, x => x.Nickname, x => x.Gender, x => x.Birthdate, x => x.GradeLevel,
            x => x.Mobile, x => x.Address, x => x.Guardian, clock);
}

public sealed class UpdateMemberHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<UpdateMember, MemberDto>
{
    public async Task<MemberDto> Handle(UpdateMember r, CancellationToken ct)
    {
        var me = current.Info;
        var (u, groupId) = await Members.LoadManageable(db, me, r.Id, tracking: true, ct);
        var today = await Members.Today(db, me.ChurchId, clock, ct);

        // Audit which fields changed, never their values (PII).
        var before = new { u.FirstName, u.LastName, u.Nickname, u.Gender, u.Birthdate, u.GradeLevel, u.Mobile, u.Address, u.JoinedOn, u.GuardianName, u.GuardianContact };
        u.UpdateDetails(r.FirstName, r.LastName, r.Nickname, r.Gender, r.Birthdate, r.GradeLevel, r.Mobile, r.Address, r.JoinedOn,
            Members.Guardian(r.Guardian, r.Birthdate, today, clock.GetUtcNow(), u), today, me.AppUserId);
        var after = new { u.FirstName, u.LastName, u.Nickname, u.Gender, u.Birthdate, u.GradeLevel, u.Mobile, u.Address, u.JoinedOn, u.GuardianName, u.GuardianContact };
        var changed = ChangedFields(before, after);

        audit.Describe("app_user", u.Id, null, new { changedFields = changed });
        return await Members.ToDto(db, u, groupId, ct);
    }

    private static List<string> ChangedFields(object before, object after) =>
        before.GetType().GetProperties()
            .Where(p => !Equals(p.GetValue(before), after.GetType().GetProperty(p.Name)!.GetValue(after)))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToList();
}

// ---------------------------------------------------------------- Status (§6.4)

public sealed record ChangeMemberStatus(Guid Id, MemberStatus Status, string? Reason) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => "member.status_change";
}

public sealed class ChangeMemberStatusValidator : AbstractValidator<ChangeMemberStatus>
{
    public ChangeMemberStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class ChangeMemberStatusHandler(IAppDbContext db, ICurrentUser current, AuditScope audit)
    : IRequestHandler<ChangeMemberStatus, MemberDto>
{
    public async Task<MemberDto> Handle(ChangeMemberStatus r, CancellationToken ct)
    {
        var (u, groupId) = await Members.LoadManageable(db, current.Info, r.Id, tracking: true, ct);
        var change = u.ChangeStatus(r.Status, current.Info.AppUserId, r.Reason);
        db.MemberStatusChanges.Add(change);
        audit.Describe("app_user", u.Id, new { status = change.FromStatus }, new { status = change.ToStatus });
        return await Members.ToDto(db, u, groupId, ct);
    }
}

// ---------------------------------------------------------------- Role (§4.2: admins; admin roles super admin only)

public sealed record ChangeMemberRole(Guid Id, UserRole Role, string? Reason) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => "member.role_change";
}

public sealed class ChangeMemberRoleValidator : AbstractValidator<ChangeMemberRole>
{
    public ChangeMemberRoleValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class ChangeMemberRoleHandler(
    IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<ChangeMemberRole, MemberDto>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MemberDto> Handle(ChangeMemberRole r, CancellationToken ct)
    {
        var me = current.Info;
        var (u, groupId) = await Members.LoadManageable(db, me, r.Id, tracking: true, ct);
        var actor = await db.Users.FirstAsync(x => x.Id == me.AppUserId, ct);

        if (!r.Role.IsAtLeast(UserRole.LEADER))
        {
            var led = await db.Groups.AsNoTracking().Where(g => g.LeaderId == u.Id && g.ArchivedAt == null).Select(g => g.Name).FirstOrDefaultAsync(ct);
            if (led is not null) throw new ConflictException($"{u.DisplayName} still leads {led}. Assign another leader there first.");
        }

        var change = u.ChangeRole(r.Role, actor, r.Reason);
        db.RoleChanges.Add(change);
        // Outbox row: the worker delivers in-app and email (§6.7, Phase 9).
        db.Notifications.Add(Notification.Create(me.ChurchId, u.Id, "role.changed",
            JsonSerializer.Serialize(new { from = change.FromRole, to = change.ToRole }, Json), ["in_app", "email"], clock.GetUtcNow()));
        Members.EvictCache(uow, users, u.AuthUserId); // roles are read from the cache, so the change applies on the next request

        audit.Describe("app_user", u.Id, new { role = change.FromRole }, new { role = change.ToRole });
        return await Members.ToDto(db, u, groupId, ct);
    }
}

// ---------------------------------------------------------------- Move between groups (admins)

public sealed record MoveMember(Guid Id, Guid GroupId) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => "member.move";
}

public sealed class MoveMemberHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<MoveMember, MemberDto>
{
    public async Task<MemberDto> Handle(MoveMember r, CancellationToken ct)
    {
        var me = current.Info;
        var (u, fromGroup) = await Members.LoadManageable(db, me, r.Id, tracking: true, ct);
        if (fromGroup == r.GroupId) throw new ConflictException("They're already in this group.");
        if (!await db.Groups.AnyAsync(g => g.Id == r.GroupId && g.ArchivedAt == null, ct)) throw new NotFoundException();

        var today = await Members.Today(db, me.ChurchId, clock, ct);
        var membership = await db.GroupMemberships.FirstOrDefaultAsync(m => m.UserId == u.Id && m.LeftOn == null, ct);
        membership?.End(today);
        db.GroupMemberships.Add(GroupMembership.Start(me.ChurchId, r.GroupId, u.Id, today));
        Members.EvictCache(uow, users, u.AuthUserId);

        audit.Describe("app_user", u.Id, new { groupId = fromGroup }, new { groupId = r.GroupId });
        return await Members.ToDto(db, u, r.GroupId, ct);
    }
}
