using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Devotions;
using TimeCheck.Application.Features.Members;
using MemberOps = TimeCheck.Application.Features.Members.Members;
using TimeCheck.Application.Features.Reports;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Privacy;

// ---------------------------------------------------------------- Export one person's data (§8.4, RA 10173 right of access)

public sealed record PersonDataExport(
    DateTimeOffset ExportedAt,
    MemberDto Profile,
    object Consent,
    IReadOnlyList<object> Groups,
    IReadOnlyList<object> Attendance,
    IReadOnlyList<object> Devotions,
    IReadOnlyList<object> InvitesMade,
    IReadOnlyList<object> RoleChanges,
    IReadOnlyList<object> StatusChanges,
    IReadOnlyList<object> Notifications);

/// <summary>Everything TimeCheck holds about one person, as JSON. Admins only; audited.</summary>
public sealed record ExportPersonData(Guid Id) : IRequest<PersonDataExport>, IAuditedRequest
{
    public string AuditAction => "member.data_export";
}

public sealed class ExportPersonDataHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock)
    : IRequestHandler<ExportPersonData, PersonDataExport>
{
    public async Task<PersonDataExport> Handle(ExportPersonData r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var (u, groupId) = await MemberOps.LoadManageable(db, me, r.Id, tracking: false, ct);

        var groups = await (from m in db.GroupMemberships.AsNoTracking()
                            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
                            where m.UserId == u.Id
                            orderby m.JoinedOn
                            select new { Group = g.Name, m.JoinedOn, m.LeftOn }).ToListAsync(ct);
        var attendance = await (from a in db.Attendance.AsNoTracking()
                                join e in db.Events.AsNoTracking() on a.EventId equals e.Id
                                where a.UserId == u.Id
                                orderby e.StartsAt
                                select new { e.Title, e.StartsAt, status = a.Status.ToString(), a.Note, a.MarkedAt }).ToListAsync(ct);
        var devotions = await db.Devotions.AsNoTracking().Where(d => d.UserId == u.Id).OrderBy(d => d.DevotionDate)
            .Select(d => new
            {
                d.DevotionDate, slot = d.Slot.ToString(), text = d.TextContent, hasImage = d.ImagePath != null, d.SubmittedAt,
                aiStatus = d.AiStatus.ToString(), d.AiTextScore, d.AiImageScore, d.ReviewNote, d.ReviewedAt,
            }).ToListAsync(ct);
        var invites = await (from i in db.Invites.AsNoTracking()
                             join e in db.Events.AsNoTracking() on i.EventId equals e.Id
                             where i.InviterId == u.Id && i.DeletedAt == null
                             orderby i.CreatedAt
                             select new { Guest = i.GuestName, Event = e.Title, Status = i.Status.ToString(), i.CreatedAt }).ToListAsync(ct);
        var roles = await db.RoleChanges.AsNoTracking().Where(c => c.UserId == u.Id).OrderBy(c => c.ChangedAt)
            .Select(c => new { From = c.FromRole.ToString(), To = c.ToRole.ToString(), c.Reason, c.ChangedAt }).ToListAsync(ct);
        var statuses = await db.MemberStatusChanges.AsNoTracking().Where(c => c.UserId == u.Id).OrderBy(c => c.ChangedAt)
            .Select(c => new { From = c.FromStatus.ToString(), To = c.ToStatus.ToString(), c.Reason, c.ChangedAt }).ToListAsync(ct);
        var notifications = await db.Notifications.AsNoTracking().Where(n => n.UserId == u.Id).OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Type, n.CreatedAt, n.ReadAt }).ToListAsync(ct);

        audit.Describe("app_user", u.Id, null, null);
        return new PersonDataExport(clock.GetUtcNow(), await MemberOps.ToDto(db, u, groupId, ct),
            new { u.PrivacyConsentVersion, u.PrivacyConsentAt, u.GuardianConsentAt, settings = JsonDocument.Parse(u.Settings).RootElement },
            groups, attendance, devotions, invites, roles, statuses, notifications);
    }
}

// ---------------------------------------------------------------- Erase one person (§8.4, right to erasure)

/// <summary>Removes a deactivated person's personal data and sign-in. Their attendance stays, counted under "Removed member".</summary>
public sealed record ErasePerson(Guid Id) : IRequest<bool>, IAuditedRequest
{
    public string AuditAction => "member.erase";
}

public sealed class ErasePersonHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, PersonEraser eraser)
    : IRequestHandler<ErasePerson, bool>
{
    public async Task<bool> Handle(ErasePerson r, CancellationToken ct)
    {
        var me = current.Info;
        if (!me.IsAdmin()) throw new ForbiddenException();
        var (u, _) = await MemberOps.LoadManageable(db, me, r.Id, tracking: true, ct);
        foreach (var effect in await eraser.EraseAsync(u, me.AppUserId, ct)) uow.AfterCommit(effect);
        // Only the fact of the erasure is kept; no personal details in the audit row.
        audit.Describe("app_user", u.Id, null, new { erased = true });
        return true;
    }
}

/// <summary>
/// The erasure itself, shared by the admin action and the retention job. Changes are tracked, not saved; the returned
/// actions (file and sign-in deletion) run after the caller commits, so a failed save leaves everything as it was.
/// </summary>
public sealed class PersonEraser(IAppDbContext db, IFileStorage storage, IAuthAdmin authAdmin, IUserDirectory users, TimeProvider clock)
{
    public async Task<List<Func<Task>>> EraseAsync(AppUser u, Guid? actorId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var authUserId = u.AuthUserId;
        u.Erase(now, actorId);
        var effects = new List<Func<Task>>();

        // The person leaves any group they're still in, so they drop off rosters; past membership stays for reports.
        var tz = await db.Churches.IgnoreQueryFilters().Where(c => c.Id == u.ChurchId).Select(c => c.Timezone).FirstAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(tz)).DateTime);
        foreach (var m in await db.GroupMemberships.IgnoreQueryFilters().Where(m => m.UserId == u.Id && m.LeftOn == null).ToListAsync(ct))
            m.End(today);

        foreach (var d in await db.Devotions.IgnoreQueryFilters().Where(d => d.UserId == u.Id).ToListAsync(ct))
        {
            if (d.ImagePath is { } path) effects.Add(() => storage.DeleteAsync(DevotionAccess.Bucket, path, CancellationToken.None));
            d.Redact();
        }

        foreach (var j in await db.ReportJobs.IgnoreQueryFilters().Where(j => j.RequestedBy == u.Id).ToListAsync(ct))
        {
            if (j.FilePath is { } path) effects.Add(() => storage.DeleteAsync(ReportExports.Bucket, path, CancellationToken.None));
            db.ReportJobs.Remove(j);
        }

        db.Notifications.RemoveRange(await db.Notifications.IgnoreQueryFilters().Where(n => n.UserId == u.Id).ToListAsync(ct));
        db.PushSubscriptions.RemoveRange(await db.PushSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ToListAsync(ct));

        if (authUserId is { } id)
        {
            effects.Add(() => users.InvalidateAsync(id, CancellationToken.None));
            effects.Add(() => authAdmin.DeleteUserAsync(id, CancellationToken.None));
        }
        return effects;
    }
}

// ---------------------------------------------------------------- Retention (§8.4)

/// <summary>
/// Nightly, per church settings (`retention`): erases people deactivated longer than `deactivatedUserYears` (default 3) and
/// deletes audit rows older than `auditLogDays` (default 400, never under 365). Guest invites have their own purge.
/// </summary>
public sealed class RetentionJob(IAppDbContext db, PersonEraser eraser, TimeProvider clock)
{
    public const int DefaultDeactivatedYears = 3;
    public const int DefaultAuditDays = 400;
    public const int MinAuditDays = 365;

    public sealed record Policy(int DeactivatedUserYears, int AuditLogDays);

    public static Policy Read(string settingsJson)
    {
        var years = DefaultDeactivatedYears;
        var days = DefaultAuditDays;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty("retention", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                if (r.TryGetProperty("deactivatedUserYears", out var y) && y.TryGetInt32(out var yv) && yv >= 1) years = yv;
                if (r.TryGetProperty("auditLogDays", out var a) && a.TryGetInt32(out var av)) days = Math.Max(av, MinAuditDays);
            }
        }
        catch (JsonException) { }
        return new Policy(years, days);
    }

    public async Task<(int Erased, int AuditRows)> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        int erased = 0, auditRows = 0;
        foreach (var church in await db.Churches.AsNoTracking().IgnoreQueryFilters().ToListAsync(ct))
        {
            var policy = Read(church.Settings);
            var cutoff = now.AddYears(-policy.DeactivatedUserYears);
            var due = await db.Users.IgnoreQueryFilters()
                .Where(u => u.ChurchId == church.Id && u.DeletedAt == null && u.DeactivatedAt != null && u.DeactivatedAt < cutoff)
                .OrderBy(u => u.DeactivatedAt).Take(100).ToListAsync(ct);
            foreach (var u in due)
            {
                var effects = await eraser.EraseAsync(u, null, ct);
                await db.SaveChangesAsync(ct);
                // shortcut: a failed file or sign-in deletion is not retried (the person is already erased); a Storage sweep would catch strays.
                foreach (var effect in effects)
                {
                    try { await effect(); }
                    catch (Exception e) when (e is not OperationCanceledException) { /* best effort, like the request pipeline */ }
                }
                erased++;
            }
            auditRows += await db.PurgeAuditLogsAsync(church.Id, now.AddDays(-policy.AuditLogDays), ct);
        }
        return (erased, auditRows);
    }
}
