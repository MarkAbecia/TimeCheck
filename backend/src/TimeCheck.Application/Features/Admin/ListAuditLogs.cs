using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;

namespace TimeCheck.Application.Features.Admin;

public sealed record AuditLogDto(Guid Id, Guid? ActorId, string Action, string? EntityType, string? EntityId,
    string? Before, string? After, string? CorrelationId, DateTimeOffset OccurredAt);

/// <summary>Super Admin audit viewer (§6.10). Keyset-paginated, newest first.</summary>
public sealed record ListAuditLogs(
    Guid? ActorId, string? Action, string? EntityType, DateTimeOffset? From, DateTimeOffset? To,
    int Limit = 50, string? Cursor = null) : IRequest<CursorPage<AuditLogDto>>;

public sealed class ListAuditLogsValidator : AbstractValidator<ListAuditLogs>
{
    public ListAuditLogsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 200);
        RuleFor(x => x.Action).MaximumLength(100);
        RuleFor(x => x.EntityType).MaximumLength(100);
    }
}

public sealed class ListAuditLogsHandler(IAppDbContext db) : IRequestHandler<ListAuditLogs, CursorPage<AuditLogDto>>
{
    public async Task<CursorPage<AuditLogDto>> Handle(ListAuditLogs r, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (r.ActorId is { } actor) q = q.Where(a => a.ActorId == actor);
        if (!string.IsNullOrEmpty(r.Action)) q = q.Where(a => a.Action == r.Action);
        if (!string.IsNullOrEmpty(r.EntityType)) q = q.Where(a => a.EntityType == r.EntityType);
        if (r.From is { } from) q = q.Where(a => a.OccurredAt >= from);
        if (r.To is { } to) q = q.Where(a => a.OccurredAt < to);
        if (Cursor.Decode(r.Cursor) is var (at, id))
            q = q.Where(a => a.OccurredAt < at || (a.OccurredAt == at && a.Id.CompareTo(id) < 0));

        var rows = await q.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(r.Limit + 1)
            .Select(a => new AuditLogDto(a.Id, a.ActorId, a.Action, a.EntityType, a.EntityId, a.Before, a.After, a.CorrelationId, a.OccurredAt))
            .ToListAsync(ct);

        var next = rows.Count > r.Limit ? Cursor.Encode(rows[r.Limit - 1].OccurredAt, rows[r.Limit - 1].Id) : null;
        return new CursorPage<AuditLogDto>(rows.Take(r.Limit).ToList(), next);
    }
}
