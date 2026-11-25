using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Invites;
using TimeCheck.Application.Features.Privacy;
using TimeCheck.Application.Features.Reports;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Admin;

/// <summary>
/// Super Admin settings (§6.10), first pass: age brackets (Member Report) and retention periods (§8.4). Values are read
/// through the same readers the reports and purge jobs use, so the screen shows what is in effect.
/// </summary>
public sealed record RetentionSettings(int GuestInviteDays, int DeactivatedUserYears, int AuditLogDays);

public sealed record GeneralSettings(IReadOnlyList<int> AgeBrackets, RetentionSettings Retention);

public sealed record GetChurchSettings : IRequest<GeneralSettings>;

public sealed record UpdateChurchSettings(IReadOnlyList<int> AgeBrackets, RetentionSettings Retention) : IRequest<GeneralSettings>, IAuditedRequest
{
    public string AuditAction => "settings.update";
}

public sealed class UpdateChurchSettingsValidator : AbstractValidator<UpdateChurchSettings>
{
    public UpdateChurchSettingsValidator()
    {
        RuleFor(x => x.AgeBrackets).NotNull()
            .Must(b => b is { Count: >= 1 and <= 8 }).WithMessage("Use between 1 and 8 age limits.")
            .Must(b => b is null || b.All(a => a is >= 1 and <= 120)).WithMessage("Each age limit is between 1 and 120.")
            .Must(b => b is null || b.Zip(b.Skip(1)).All(z => z.First < z.Second)).WithMessage("Each age limit is higher than the one before.");
        RuleFor(x => x.Retention).NotNull();
        RuleFor(x => x.Retention.GuestInviteDays).InclusiveBetween(1, 365).When(x => x.Retention is not null);
        RuleFor(x => x.Retention.DeactivatedUserYears).InclusiveBetween(1, 10).When(x => x.Retention is not null);
        RuleFor(x => x.Retention.AuditLogDays).InclusiveBetween(RetentionJob.MinAuditDays, 3650).When(x => x.Retention is not null);
    }
}

internal static class ChurchSettings
{
    public static GeneralSettings Read(string json)
    {
        var policy = RetentionJob.Read(json);
        return new GeneralSettings(ReportBuilder.AgeBrackets(json),
            new RetentionSettings(InviteAccess.PurgeAfterDays(json), policy.DeactivatedUserYears, policy.AuditLogDays));
    }

    public static void RequireSuperAdmin(CurrentUserInfo me)
    {
        if (!me.Role.IsAtLeast(UserRole.SUPER_ADMIN)) throw new ForbiddenException();
    }
}

public sealed class GetChurchSettingsHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<GetChurchSettings, GeneralSettings>
{
    public async Task<GeneralSettings> Handle(GetChurchSettings r, CancellationToken ct)
    {
        var me = current.Info;
        ChurchSettings.RequireSuperAdmin(me);
        var json = await db.Churches.AsNoTracking().Where(c => c.Id == me.ChurchId).Select(c => c.Settings).FirstAsync(ct);
        return ChurchSettings.Read(json);
    }
}

public sealed class UpdateChurchSettingsHandler(IAppDbContext db, ICurrentUser current, AuditScope audit)
    : IRequestHandler<UpdateChurchSettings, GeneralSettings>
{
    public async Task<GeneralSettings> Handle(UpdateChurchSettings r, CancellationToken ct)
    {
        var me = current.Info;
        ChurchSettings.RequireSuperAdmin(me);
        var church = await db.Churches.FirstAsync(c => c.Id == me.ChurchId, ct);
        var before = ChurchSettings.Read(church.Settings);

        // Only these keys change; the rest of the jsonb (devotion windows, AI, ...) is kept as it is.
        var root = JsonNode.Parse(church.Settings) as JsonObject ?? [];
        root["ageBrackets"] = new JsonArray([.. r.AgeBrackets.Select(a => (JsonNode)a)]);
        if (root["retention"] is not JsonObject retention) root["retention"] = retention = [];
        retention["guestInviteDays"] = r.Retention.GuestInviteDays;
        retention["deactivatedUserYears"] = r.Retention.DeactivatedUserYears;
        retention["auditLogDays"] = r.Retention.AuditLogDays;
        church.ReplaceSettings(root.ToJsonString());

        var after = ChurchSettings.Read(church.Settings);
        audit.Describe("church", church.Id, before, after);
        return after;
    }
}
