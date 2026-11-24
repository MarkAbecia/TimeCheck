using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;

namespace TimeCheck.Application.Features.Me;

/// <summary>
/// Sets the signed-in user's password through the Supabase admin API and clears the forced-change flag (§6.1).
/// Serves both the first-login change and the reset link (the BFF signs the user in from the link first).
/// </summary>
public sealed record ChangeMyPassword(string NewPassword) : IRequest<MeDto>, IAuditedRequest
{
    public string AuditAction => "auth.password_change";

    // Keep the password out of anything that stringifies the request (logs, exceptions).
    public override string ToString() => "ChangeMyPassword { NewPassword = *** }";
}

public sealed class ChangeMyPasswordValidator : AbstractValidator<ChangeMyPassword>
{
    public ChangeMyPasswordValidator()
    {
        // Mirrors Supabase auth (database/supabase/config.toml) and the frontend schema.
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(72)
            .Must(p => p is not null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("Include at least one letter and one number.");
    }
}

public sealed class ChangeMyPasswordHandler(
    IAppDbContext db, ICurrentUser current, IAuthAdmin authAdmin, AuditScope audit, IAccountPolicy policy)
    : IRequestHandler<ChangeMyPassword, MeDto>
{
    public async Task<MeDto> Handle(ChangeMyPassword request, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == current.Info.AppUserId, ct) ?? throw new NotFoundException();
        if (u.AuthUserId is not { } authUserId) throw new ConflictException("This account has no sign-in yet.");

        await authAdmin.SetPasswordAsync(authUserId, request.NewPassword, ct);
        var forced = u.MustChangePassword;
        u.PasswordChanged();
        audit.Describe("app_user", u.Id, null, new { forced });
        return await MeQuery.Load(db, current.Info, policy, ct, u);
    }
}

/// <summary>Records acceptance of the current privacy notice (§8.4). Only the current version can be accepted.</summary>
public sealed record AcceptPrivacyNotice(string Version) : IRequest<MeDto>, IAuditedRequest
{
    public string AuditAction => "privacy.consent";
}

public sealed class AcceptPrivacyNoticeValidator : AbstractValidator<AcceptPrivacyNotice>
{
    public AcceptPrivacyNoticeValidator(IAccountPolicy policy)
    {
        RuleFor(x => x.Version).Equal(policy.PrivacyConsentVersion)
            .WithMessage("That privacy notice is out of date. Reload and try again.");
    }
}

public sealed class AcceptPrivacyNoticeHandler(
    IAppDbContext db, ICurrentUser current, AuditScope audit, IAccountPolicy policy, TimeProvider clock)
    : IRequestHandler<AcceptPrivacyNotice, MeDto>
{
    public async Task<MeDto> Handle(AcceptPrivacyNotice request, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == current.Info.AppUserId, ct) ?? throw new NotFoundException();
        var before = u.PrivacyConsentVersion;
        u.RecordPrivacyConsent(request.Version, clock.GetUtcNow());
        audit.Describe("app_user", u.Id, new { version = before }, new { version = request.Version });
        return await MeQuery.Load(db, current.Info, policy, ct, u);
    }
}
