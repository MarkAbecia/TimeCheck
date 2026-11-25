using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Reports;

// ---------------------------------------------------------------- Exports (§6.8)

/// <summary>Scope is the report's filters in words, saved so the list can say which export is which.</summary>
public sealed record ReportJobDto(
    Guid Id, string Type, string Title, string Format, ReportJobStatus Status, IReadOnlyList<string> Scope,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? Error);

internal sealed record ExportParams(string Format, ReportFilters Filters, IReadOnlyList<string> Scope);

/// <summary>
/// Makes a CSV, XLSX or PDF of a report. Small ones are ready in the response; bigger ones go to the Worker, which notifies
/// the person when the file is ready. Files stay private and are kept 24 hours.
/// </summary>
public sealed record ExportReport(string Type, string Format, ReportFilters Filters) : IRequest<ReportJobDto>, IAuditedRequest
{
    public string AuditAction => "report.export";
}

public sealed class ExportReportValidator : AbstractValidator<ExportReport>
{
    public ExportReportValidator()
    {
        RuleFor(x => x.Format).Must(f => ReportFiles.Formats.Contains(f)).WithMessage("Choose CSV, XLSX or PDF.");
        RuleFor(x => x.Filters).NotNull().SetValidator(new ReportFiltersValidator());
    }
}

public sealed class ExportReportHandler(
    ReportBuilder builder, IAppDbContext db, ICurrentUser current, IFileStorage storage, UnitOfWork uow, AuditScope audit, TimeProvider clock)
    : IRequestHandler<ExportReport, ReportJobDto>
{
    public async Task<ReportJobDto> Handle(ExportReport r, CancellationToken ct)
    {
        var me = current.Info;
        var report = await builder.RunCachedAsync(me, r.Type, r.Filters, ct); // also the access check
        var job = ReportJob.Queue(me.ChurchId, me.AppUserId, r.Type,
            JsonSerializer.Serialize(new ExportParams(r.Format, r.Filters, report.Scope), ReportJson.Options));
        db.ReportJobs.Add(job);

        var rows = report.Sections.Sum(s => s.Rows.Count);
        if (rows <= ReportExports.InlineRows)
        {
            var path = await ReportExports.WriteAsync(job, report, r.Format, storage, clock.GetUtcNow(), ct);
            uow.OnRollback(() => storage.DeleteAsync(ReportExports.Bucket, path, CancellationToken.None));
        }

        audit.Describe("report_job", job.Id, null, new { r.Type, r.Format, r.Filters, rows });
        return ReportExports.ToDto(job, clock.GetUtcNow());
    }
}

public sealed record ListMyReportJobs : IRequest<IReadOnlyList<ReportJobDto>>;

public sealed class ListMyReportJobsHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<ListMyReportJobs, IReadOnlyList<ReportJobDto>>
{
    public async Task<IReadOnlyList<ReportJobDto>> Handle(ListMyReportJobs request, CancellationToken ct)
    {
        var me = current.Info.AppUserId;
        var jobs = await db.ReportJobs.AsNoTracking().Where(j => j.RequestedBy == me).OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(ct);
        return jobs.Select(j => ReportExports.ToDto(j, j.CreatedAt)).ToList();
    }
}

public sealed record ReportDownloadDto(string Url);

/// <summary>A short-lived link to the person's own finished export; it ends when the file does.</summary>
public sealed record GetReportDownload(Guid Id) : IRequest<ReportDownloadDto>;

public sealed class GetReportDownloadHandler(IAppDbContext db, ICurrentUser current, IFileStorage storage, TimeProvider clock)
    : IRequestHandler<GetReportDownload, ReportDownloadDto>
{
    public async Task<ReportDownloadDto> Handle(GetReportDownload r, CancellationToken ct)
    {
        var me = current.Info.AppUserId;
        var job = await db.ReportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == r.Id && j.RequestedBy == me, ct) ?? throw new NotFoundException();
        var now = clock.GetUtcNow();
        if (job.Status != ReportJobStatus.DONE || job.FilePath is null || job.ExpiresAt is not { } expires || expires <= now)
            throw new DomainException("report.not_ready", job.Status is ReportJobStatus.QUEUED or ReportJobStatus.RUNNING
                ? "This file isn't ready yet." : "This file has expired. Export the report again.");

        var ttl = expires - now < TimeSpan.FromMinutes(10) ? expires - now : TimeSpan.FromMinutes(10);
        var url = await storage.SignedUrlAsync(ReportExports.Bucket, job.FilePath, ttl, ct);
        var name = $"timecheck-{job.Type}-{job.CreatedAt.UtcDateTime:yyyy-MM-dd}.{Path.GetExtension(job.FilePath).TrimStart('.')}";
        return new ReportDownloadDto($"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}download={Uri.EscapeDataString(name)}");
    }
}

/// <summary>
/// Worker, every minute: makes queued exports (as the person who asked, with their access today), tells them when each is
/// ready, and deletes files past their 24 hours.
/// </summary>
public sealed class ReportExports(IAppDbContext db, ReportBuilder builder, IFileStorage storage, TimeProvider clock, ILogger<ReportExports> logger)
{
    public const string Bucket = "exports";

    /// <summary>Exports up to this many rows are made in the request; bigger ones go to the Worker.</summary>
    public const int InlineRows = 2000;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var expired = await db.ReportJobs.IgnoreQueryFilters().Where(j => j.Status == ReportJobStatus.DONE && j.ExpiresAt <= now).Take(100).ToListAsync(ct);
        foreach (var job in expired)
        {
            try
            {
                await storage.DeleteAsync(Bucket, job.FilePath!, ct);
                job.Expire();
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Couldn't delete export {JobId}; will retry", job.Id); }
        }

        // A Worker that died mid-export leaves the job running; give up on it after half an hour.
        var stuckBefore = now.AddMinutes(-30);
        foreach (var job in await db.ReportJobs.IgnoreQueryFilters().Where(j => j.Status == ReportJobStatus.RUNNING && j.UpdatedAt < stuckBefore).ToListAsync(ct))
            job.Fail("This export stopped before it finished. Export the report again.", now);
        await db.SaveChangesAsync(ct);

        var queued = await db.ReportJobs.IgnoreQueryFilters().Where(j => j.Status == ReportJobStatus.QUEUED).OrderBy(j => j.CreatedAt).Take(5).ToListAsync(ct);
        foreach (var job in queued)
        {
            job.Start();
            await db.SaveChangesAsync(ct);
            try
            {
                var p = JsonSerializer.Deserialize<ExportParams>(job.Params, ReportJson.Options)!;
                var me = await RequesterAsync(job, ct) ?? throw new ForbiddenException("Your account can't run this report any more.");
                var report = await builder.RunAsync(me, job.Type, p.Filters, ct);
                await WriteAsync(job, report, p.Format, storage, clock.GetUtcNow(), ct);
                db.Notifications.Add(Notification.Create(job.ChurchId, job.RequestedBy, "report.ready",
                    JsonSerializer.Serialize(new { jobId = job.Id, title = report.Title, format = p.Format.ToUpperInvariant() }), ["in_app", "email"], now));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Export {JobId} failed", job.Id);
                job.Fail(e is ForbiddenException or NotFoundException ? e.Message : "Something went wrong making this file. Export the report again.", clock.GetUtcNow());
            }
            await db.SaveChangesAsync(ct);
        }
        return queued.Count;
    }

    /// <summary>The requester as they are now: a demoted leader or a deactivated account gets nothing.</summary>
    private async Task<CurrentUserInfo?> RequesterAsync(ReportJob job, CancellationToken ct)
    {
        var u = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == job.RequestedBy && x.ChurchId == job.ChurchId && x.DeactivatedAt == null && x.DeletedAt == null, ct);
        if (u is null) return null;
        var led = await db.Groups.IgnoreQueryFilters().AsNoTracking().Where(g => g.LeaderId == u.Id && g.ArchivedAt == null).Select(g => g.Id).ToListAsync(ct);
        var group = await db.GroupMemberships.IgnoreQueryFilters().AsNoTracking().Where(m => m.UserId == u.Id && m.LeftOn == null)
            .Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);
        return new CurrentUserInfo(u.Id, u.ChurchId, u.Role, true, led, group);
    }

    internal static async Task<string> WriteAsync(ReportJob job, ReportDto report, string format, IFileStorage storage, DateTimeOffset now, CancellationToken ct)
    {
        var file = ReportFiles.Write(report, format);
        var path = $"{job.ChurchId}/{job.Id}.{file.Extension}";
        await storage.UploadAsync(Bucket, path, file.Bytes, file.ContentType, ct);
        job.Complete(path, now);
        return path;
    }

    internal static ReportJobDto ToDto(ReportJob j, DateTimeOffset createdAt)
    {
        ExportParams? p = null;
        try { p = JsonSerializer.Deserialize<ExportParams>(j.Params, ReportJson.Options); }
        catch (JsonException) { /* older or hand-made row: show it without its details */ }
        return new ReportJobDto(j.Id, j.Type, ReportCatalog.Find(j.Type)?.Title ?? j.Type, p?.Format ?? "", j.Status, p?.Scope ?? [],
            createdAt, j.ExpiresAt, j.Error);
    }
}
