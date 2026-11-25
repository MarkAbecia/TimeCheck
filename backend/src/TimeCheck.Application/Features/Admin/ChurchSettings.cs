using System.Globalization;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Devotions;
using TimeCheck.Application.Features.Invites;
using TimeCheck.Application.Features.Privacy;
using TimeCheck.Application.Features.Reports;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Admin;

/// <summary>
/// Super Admin settings (§6.10): age brackets (Member Report), retention periods (§8.4), devotion windows and reminder
/// times (§6.3, §15 Q4), and AI detection (§6.3). Values are read through the same readers the reports, jobs and devotion
/// pages use, so the screen shows what is in effect.
/// </summary>
public sealed record RetentionSettings(int GuestInviteDays, int DeactivatedUserYears, int AuditLogDays);

/// <summary>Church wall times as "HH:mm". A Night closing at or before its opening closes the next morning.</summary>
public sealed record DevotionTimes(string DayOpens, string DayCloses, string DayReminder, string NightOpens, string NightCloses, string NightReminder);

/// <summary>Bands: a score under LikelyHumanBelow is "Likely human", over LikelyAiAbove "Likely AI", between "Uncertain".</summary>
public sealed record AiSettings(bool Enabled, int LikelyHumanBelow, int LikelyAiAbove, bool MembersSeeOwnScore);

public sealed record GeneralSettings(IReadOnlyList<int> AgeBrackets, RetentionSettings Retention, DevotionTimes Devotion, AiSettings Ai);

public sealed record GetChurchSettings : IRequest<GeneralSettings>;

/// <summary>Devotion and Ai may be left out (null) to keep them as they are.</summary>
public sealed record UpdateChurchSettings(
    IReadOnlyList<int> AgeBrackets, RetentionSettings Retention, DevotionTimes? Devotion = null, AiSettings? Ai = null)
    : IRequest<GeneralSettings>, IAuditedRequest
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

        When(x => x.Devotion is not null, () =>
        {
            RuleFor(x => x.Devotion!).Must(d => new[] { d.DayOpens, d.DayCloses, d.DayReminder, d.NightOpens, d.NightCloses, d.NightReminder }
                    .All(t => ChurchSettings.Time(t) is not null))
                .WithMessage("Times are HH:mm, from 00:00 to 23:59.")
                .DependentRules(() =>
                {
                    RuleFor(x => x.Devotion!).Must(d => ChurchSettings.Time(d.DayOpens) < ChurchSettings.Time(d.DayCloses))
                        .WithMessage("Day closes after it opens, on the same day.");
                    RuleFor(x => x.Devotion!).Must(d => ChurchSettings.Time(d.NightOpens) != ChurchSettings.Time(d.NightCloses))
                        .WithMessage("Night opens and closes at different times.");
                    RuleFor(x => x.Devotion!).Must(d => Within(d.DayReminder, d.DayOpens, d.DayCloses))
                        .WithMessage("The Day reminder is sent while Day is open.");
                    // The reminder job sends once its time has passed today and the slot is open, so a Night reminder
                    // after midnight would go out the evening before, at opening time.
                    RuleFor(x => x.Devotion!).Must(d => ChurchSettings.Time(d.NightReminder) >= ChurchSettings.Time(d.NightOpens) &&
                            (ChurchSettings.Time(d.NightCloses) <= ChurchSettings.Time(d.NightOpens) || Within(d.NightReminder, d.NightOpens, d.NightCloses)))
                        .WithMessage("The Night reminder is sent while Night is open, before midnight.");
                });
        });

        When(x => x.Ai is not null, () =>
        {
            RuleFor(x => x.Ai!.LikelyHumanBelow).InclusiveBetween(1, 99);
            RuleFor(x => x.Ai!.LikelyAiAbove).InclusiveBetween(1, 99);
            RuleFor(x => x.Ai!).Must(a => a.LikelyHumanBelow <= a.LikelyAiAbove)
                .WithMessage("The 'likely human' limit is at or below the 'likely AI' limit.");
        });
    }

    private static bool Within(string t, string from, string to) =>
        ChurchSettings.Time(t) >= ChurchSettings.Time(from) && ChurchSettings.Time(t) <= ChurchSettings.Time(to);
}

internal static class ChurchSettings
{
    public static TimeOnly? Time(string? s) =>
        TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static GeneralSettings Read(string json)
    {
        var policy = RetentionJob.Read(json);
        var d = DevotionSettings.Read(json);
        var w = d.Windows;
        return new GeneralSettings(ReportBuilder.AgeBrackets(json),
            new RetentionSettings(InviteAccess.PurgeAfterDays(json), policy.DeactivatedUserYears, policy.AuditLogDays),
            new DevotionTimes(Hm(w.DayOpens), Hm(w.DayCloses), Hm(d.DayReminder), Hm(w.NightOpens), Hm(w.NightCloses), Hm(d.NightReminder)),
            new AiSettings(d.AiEnabled, d.LikelyHumanBelow, d.LikelyAiAbove, d.MembersSeeOwnScore));
    }

    /// <summary>The named object in the settings, created when missing; other keys in it are kept.</summary>
    public static JsonObject Section(JsonObject root, string name)
    {
        if (root[name] is not JsonObject section) root[name] = section = [];
        return section;
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

        // Only these keys change; the rest of the jsonb (attendance window, Week of Fire, AI provider, ...) is kept as it is.
        var root = JsonNode.Parse(church.Settings) as JsonObject ?? [];
        root["ageBrackets"] = new JsonArray([.. r.AgeBrackets.Select(a => (JsonNode)a)]);
        var retention = ChurchSettings.Section(root, "retention");
        retention["guestInviteDays"] = r.Retention.GuestInviteDays;
        retention["deactivatedUserYears"] = r.Retention.DeactivatedUserYears;
        retention["auditLogDays"] = r.Retention.AuditLogDays;
        if (r.Devotion is { } d)
        {
            var dev = ChurchSettings.Section(root, "devotion");
            dev["dayOpens"] = d.DayOpens;
            dev["dayCloses"] = d.DayCloses;
            dev["dayReminder"] = d.DayReminder;
            dev["nightOpens"] = d.NightOpens;
            dev["nightCloses"] = d.NightCloses;
            dev["nightReminder"] = d.NightReminder;
        }
        if (r.Ai is { } a)
        {
            var ai = ChurchSettings.Section(root, "ai");
            ai["enabled"] = a.Enabled;
            ai["likelyHumanBelow"] = a.LikelyHumanBelow;
            ai["likelyAiAbove"] = a.LikelyAiAbove;
            ai["membersSeeOwnScore"] = a.MembersSeeOwnScore;
        }
        church.ReplaceSettings(root.ToJsonString());

        var after = ChurchSettings.Read(church.Settings);
        audit.Describe("church", church.Id, before, after);
        return after;
    }
}
