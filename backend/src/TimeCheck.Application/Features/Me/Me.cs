using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Me;

public sealed record MeDto(
    Guid Id, string FirstName, string LastName, string? Nickname, string DisplayName, string Email,
    UserRole Role, MemberStatus Status, string? Mobile, string? Address, Guid? GroupId,
    IReadOnlyList<Guid> LedGroupIds, bool MustChangePassword, string? PrivacyConsentVersion)
{
    public static MeDto From(Domain.Entities.AppUser u, CurrentUserInfo info) => new(
        u.Id, u.FirstName, u.LastName, u.Nickname, u.DisplayName, u.Email, u.Role, u.Status,
        u.Mobile, u.Address, info.GroupId, info.LedGroupIds, u.MustChangePassword, u.PrivacyConsentVersion);
}

public sealed record GetMe : IRequest<MeDto>;

public sealed class GetMeHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<GetMe, MeDto>
{
    public async Task<MeDto> Handle(GetMe request, CancellationToken ct)
    {
        var info = current.Info;
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == info.AppUserId, ct) ?? throw new NotFoundException();
        return MeDto.From(u, info);
    }
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

public sealed class UpdateMyProfileHandler(IAppDbContext db, ICurrentUser current, AuditScope audit)
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
        return MeDto.From(u, current.Info);
    }
}
