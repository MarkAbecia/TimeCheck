using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Me;

/// <summary>
/// The signed-in user plus what the client needs to route them: the onboarding steps still owed
/// (password change, MFA for admins, privacy consent) and the names shown in the shell.
/// </summary>
public sealed record MeDto(
    Guid Id, string FirstName, string LastName, string? Nickname, string DisplayName, string Email,
    UserRole Role, MemberStatus Status, string? Mobile, string? Address, Guid? GroupId,
    IReadOnlyList<Guid> LedGroupIds, bool MustChangePassword, string? PrivacyConsentVersion,
    string RequiredPrivacyConsentVersion, bool MfaRequired, string ChurchName, string? GroupName);

internal static class MeQuery
{
    /// <summary>Reads the tracked or stored user (pass a tracked entity to reflect unsaved changes).</summary>
    public static async Task<MeDto> Load(IAppDbContext db, CurrentUserInfo info, IAccountPolicy policy, CancellationToken ct,
        Domain.Entities.AppUser? user = null)
    {
        var u = user ?? await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == info.AppUserId, ct) ?? throw new NotFoundException();
        var churchName = await db.Churches.AsNoTracking().Where(c => c.Id == info.ChurchId).Select(c => c.Name).FirstAsync(ct);
        // The shell shows the group a leader leads, else the member's own group.
        var groupId = info.LedGroupIds.Count > 0 ? info.LedGroupIds[0] : info.GroupId;
        var groupName = groupId is null ? null
            : await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        return new MeDto(
            u.Id, u.FirstName, u.LastName, u.Nickname, u.DisplayName, u.Email, u.Role, u.Status,
            u.Mobile, u.Address, info.GroupId, info.LedGroupIds, u.MustChangePassword, u.PrivacyConsentVersion,
            policy.PrivacyConsentVersion, policy.RequireAdminMfa && u.Role.IsAtLeast(UserRole.ADMIN), churchName, groupName);
    }
}

public sealed record GetMe : IRequest<MeDto>;

public sealed class GetMeHandler(IAppDbContext db, ICurrentUser current, IAccountPolicy policy) : IRequestHandler<GetMe, MeDto>
{
    public Task<MeDto> Handle(GetMe request, CancellationToken ct) => MeQuery.Load(db, current.Info, policy, ct);
}

/// <summary>Self-service fields only (§6.9). Birthdate, gender and group are leader/admin-only.</summary>
public sealed record UpdateMyProfile(string? Nickname, string? Mobile, string? Address) : IRequest<MeDto>, IAuditedRequest
{
    public string AuditAction => "profile.update";
}

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfile>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.Nickname).MaximumLength(100);
        RuleFor(x => x.Mobile).MaximumLength(20).Matches(@"^\+?[0-9 ()-]{7,20}$").When(x => !string.IsNullOrWhiteSpace(x.Mobile));
        RuleFor(x => x.Address).MaximumLength(500);
    }
}

public sealed class UpdateMyProfileHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, IAccountPolicy policy)
    : IRequestHandler<UpdateMyProfile, MeDto>
{
    public async Task<MeDto> Handle(UpdateMyProfile request, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == current.Info.AppUserId, ct) ?? throw new NotFoundException();
        // Audit records which fields changed, not their (PII) values.
        var changed = new List<string>();
        if (u.Nickname != request.Nickname) changed.Add("nickname");
        if (u.Mobile != request.Mobile) changed.Add("mobile");
        if (u.Address != request.Address) changed.Add("address");

        u.UpdateOwnProfile(request.Nickname, request.Mobile, request.Address);
        audit.Describe("app_user", u.Id, null, new { changedFields = changed });
        return await MeQuery.Load(db, current.Info, policy, ct, u);
    }
}
