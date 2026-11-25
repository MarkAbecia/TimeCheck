using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
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

// ---------------------------------------------------------------- Deactivate / reactivate (§6.10, admins)

/// <summary>Soft delete: a deactivated person can't sign in (within a minute, the user cache) and drops off rosters and reminders.</summary>
public sealed record SetMemberActive(Guid Id, bool Active) : IRequest<MemberDto>, IAuditedRequest
{
    public string AuditAction => Active ? "member.reactivate" : "member.deactivate";
}

public sealed class SetMemberActiveHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<SetMemberActive, MemberDto>
{
    public async Task<MemberDto> Handle(SetMemberActive r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var (u, groupId) = await Members.LoadManageable(db, me, r.Id, tracking: true, ct);
        if (u.DeletedAt is not null) throw new NotFoundException();
        var wasActive = u.IsActive;
        if (r.Active) u.Reactivate(me.AppUserId);
        else
        {
            var led = await db.Groups.Where(g => g.LeaderId == u.Id && g.ArchivedAt == null).Select(g => g.Name).FirstOrDefaultAsync(ct);
            if (led is not null) throw new DomainException("member.leads_group", $"They lead {led}. Choose another leader for it first.");
            u.Deactivate(clock.GetUtcNow(), me.AppUserId);
        }
        Members.EvictCache(uow, users, u.AuthUserId);
        audit.Describe("app_user", u.Id, new { active = wasActive }, new { active = r.Active });
        return await Members.ToDto(db, u, groupId, ct);
    }
}

// ---------------------------------------------------------------- Reset MFA (§6.10, admins)

/// <summary>For someone who lost their authenticator: removes their factors so the next sign-in sets up a new code.</summary>
public sealed record ResetMemberMfa(Guid Id) : IRequest<MfaResetResult>, IAuditedRequest
{
    public string AuditAction => "member.mfa_reset";
}

public sealed record MfaResetResult(int Removed);

public sealed class ResetMemberMfaHandler(IAppDbContext db, ICurrentUser current, IAuthAdmin authAdmin, AuditScope audit, TimeProvider clock)
    : IRequestHandler<ResetMemberMfa, MfaResetResult>
{
    public async Task<MfaResetResult> Handle(ResetMemberMfa r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var (u, _) = await Members.LoadManageable(db, me, r.Id, tracking: false, ct); // only a super admin resets an admin
        if (u.AuthUserId is not { } authUserId) throw new NotFoundException();

        // Supabase is outside the transaction; removing factors twice is harmless, so a failed save can simply be retried.
        var removed = await authAdmin.RemoveMfaFactorsAsync(authUserId, ct);
        if (removed > 0)
            // Tell the person, in case it wasn't them who asked (§6.7 outbox: in-app and email).
            db.Notifications.Add(Notification.Create(me.ChurchId, u.Id, "mfa.reset", "{}", ["in_app", "email"], clock.GetUtcNow()));
        audit.Describe("app_user", u.Id, null, new { factorsRemoved = removed });
        return new MfaResetResult(removed);
    }
}

// ---------------------------------------------------------------- Search (§6.10, admins)

public sealed record MemberHit(Guid Id, string DisplayName, string Email, string? GroupName, MemberStatus Status, UserRole Role, bool IsActive);

/// <summary>Name, nickname or email contains the text, or the mobile number matches exactly (it's encrypted, so by blind index).</summary>
public sealed record SearchMembers(string Q) : IRequest<IReadOnlyList<MemberHit>>
{
    public const int Limit = 20;
}

public sealed class SearchMembersValidator : AbstractValidator<SearchMembers>
{
    public SearchMembersValidator() =>
        RuleFor(x => x.Q).Must(q => q.Trim().Length is >= 2 and <= 100).WithMessage("Type at least 2 characters.").OverridePropertyName("q");
}

public sealed class SearchMembersHandler(IAppDbContext db, ICurrentUser current, IBlindIndexer index)
    : IRequestHandler<SearchMembers, IReadOnlyList<MemberHit>>
{
    public async Task<IReadOnlyList<MemberHit>> Handle(SearchMembers r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var q = r.Q.Trim();
        var like = "%" + q.ToLowerInvariant().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        var mobileIdx = q.Count(char.IsAsciiDigit) >= 7 && System.Text.RegularExpressions.Regex.IsMatch(q, Members.MobilePattern) ? index.Mobile(q) : null;
        var superAdmin = me.Role == UserRole.SUPER_ADMIN;

        // shortcut: LIKE on lower() scans app_users, fine for one church of ~1,000; add a pg_trgm index if churches grow past ~50k people.
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.Id != me.AppUserId && (superAdmin || (u.Role != UserRole.ADMIN && u.Role != UserRole.SUPER_ADMIN)))
#pragma warning disable CA1304, CA1311 // ToLower() runs as SQL lower(), not in .NET; ToLowerInvariant doesn't translate
            .Where(u => EF.Functions.Like((u.FirstName + " " + u.LastName).ToLower(), like, @"\")
                || EF.Functions.Like((u.Nickname ?? "").ToLower(), like, @"\") || EF.Functions.Like(u.Email.ToLower(), like, @"\")
                || (mobileIdx != null && EF.Property<string?>(u, BlindIndexColumns.Mobile) == mobileIdx))
#pragma warning restore CA1304, CA1311
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Take(SearchMembers.Limit)
            .Select(u => new
            {
                u.Id, u.FirstName, u.LastName, u.Nickname, u.Email, u.Status, u.Role, u.DeactivatedAt,
                Group = db.GroupMemberships.Where(m => m.UserId == u.Id && m.LeftOn == null)
                    .Join(db.Groups, m => m.GroupId, g => g.Id, (m, g) => g.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(u => new MemberHit(u.Id, string.IsNullOrWhiteSpace(u.Nickname) ? $"{u.FirstName} {u.LastName}" : u.Nickname,
            u.Email, u.Group, u.Status, u.Role, u.DeactivatedAt == null)).ToList();
    }
}

// ---------------------------------------------------------------- Force logout (§6.10, admins)

/// <summary>Signs the person out on every device: their sessions end and tokens already issued are refused.</summary>
public sealed record ForceLogout(Guid Id) : IRequest<ForceLogoutResult>, IAuditedRequest
{
    public string AuditAction => "member.force_logout";
}

public sealed record ForceLogoutResult(int Sessions);

public sealed class ForceLogoutHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<ForceLogout, ForceLogoutResult>
{
    public async Task<ForceLogoutResult> Handle(ForceLogout r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var (u, _) = await Members.LoadManageable(db, me, r.Id, tracking: true, ct); // only a super admin signs out an admin
        if (u.AuthUserId is not { } authUserId) throw new NotFoundException();

        u.RevokeSessions(clock.GetUtcNow(), me.AppUserId);
        var sessions = await db.RevokeAuthSessionsAsync(authUserId, ct); // same transaction as the stamp
        Members.EvictCache(uow, users, authUserId); // the stamp is read from the user cache

        audit.Describe("app_user", u.Id, null, new { sessionsEnded = sessions });
        return new ForceLogoutResult(sessions);
    }
}
