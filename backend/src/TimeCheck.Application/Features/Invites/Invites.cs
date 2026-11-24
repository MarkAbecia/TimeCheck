using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Events;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Invites;

// ---------------------------------------------------------------- Shapes and access (§6.5)

/// <summary>
/// One guest a member invited. Contact is PII: only the inviter, their leader and admins ever reach an invite (§6.5);
/// anyone else gets 404.
/// </summary>
public sealed record InviteDto(
    Guid Id, Guid EventId, string EventTitle, string EventTypeCode, DateTimeOffset EventStartsAt, bool EventCancelled,
    string GuestName, string? GuestContact, InviteStatus Status, Guid? ConvertedUserId,
    Guid InviterId, string InviterName, Guid? InviterGroupId, string? InviterGroupName, bool Mine, bool CanConvert);

/// <summary>An event a guest can be invited to: a Cell Meeting or Cell Celebration (§6.5).</summary>
public sealed record InviteEventDto(Guid Id, string Title, string TypeCode, DateTimeOffset StartsAt);

public sealed record MyInvitesDto(IReadOnlyList<InviteEventDto> Events, IReadOnlyList<InviteDto> Invites);

internal static class InviteAccess
{
    public static readonly string[] EventTypes = ["CELL_MEETING", "CELL_CELEBRATION"];
    public const int LogDaysBack = 7; // a guest can be logged up to a week after the event
    public const int LogDaysAhead = 30;

    /// <summary>How long an unconverted guest's details are kept after the event (§6.5, §8.4; church setting, default 90 days).</summary>
    public static int PurgeAfterDays(string settings)
    {
        using var doc = JsonDocument.Parse(settings);
        return doc.RootElement.TryGetProperty("invites", out var i) && i.ValueKind == JsonValueKind.Object &&
            i.TryGetProperty("purgeAfterDays", out var v) && v.TryGetInt32(out var days) && days > 0 ? days : 90;
    }

    /// <summary>Invites this user may see: their own, those of members of groups they lead, or all for admins.</summary>
    public static IQueryable<Invite> Reachable(IAppDbContext db, CurrentUserInfo me)
    {
        if (me.IsAdmin()) return db.Invites;
        var (uid, led) = (me.AppUserId, me.LedGroupIds.ToList());
        return db.Invites.Where(i => i.InviterId == uid ||
            db.GroupMemberships.Any(m => m.UserId == i.InviterId && m.LeftOn == null && led.Contains(m.GroupId)));
    }

    public static async Task<Invite> Load(IAppDbContext db, CurrentUserInfo me, Guid id, CancellationToken ct) =>
        await Reachable(db, me).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException();

    /// <summary>Shapes loaded invites (tracked or not, saved or not) with their event, inviter and inviter's group.</summary>
    public static async Task<List<InviteDto>> Dtos(IAppDbContext db, CurrentUserInfo me, IReadOnlyList<Invite> invites, CancellationToken ct)
    {
        if (invites.Count == 0) return [];
        var eventIds = invites.Select(i => i.EventId).Distinct().ToList();
        var userIds = invites.Select(i => i.InviterId).Distinct().ToList();
        var events = await (
            from e in db.Events.AsNoTracking()
            join t in db.EventTypes.AsNoTracking() on e.EventTypeId equals t.Id
            where eventIds.Contains(e.Id)
            select new { e.Id, e.Title, t.Code, e.StartsAt, Cancelled = e.CancelledAt != null }).ToDictionaryAsync(e => e.Id, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id, u.FirstName, u.LastName, u.Nickname,
                GroupId = db.GroupMemberships.Where(m => m.UserId == u.Id && m.LeftOn == null).Select(m => (Guid?)m.GroupId).FirstOrDefault(),
            }).ToDictionaryAsync(u => u.Id, ct);
        var groupIds = users.Values.Where(u => u.GroupId != null).Select(u => u.GroupId!.Value).Distinct().ToList();
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return invites.Select(i =>
        {
            var e = events[i.EventId];
            var u = users[i.InviterId];
            var leads = u.GroupId is { } g && (me.IsAdmin() || me.LedGroupIds.Contains(g));
            return new InviteDto(i.Id, i.EventId, e.Title, e.Code, e.StartsAt, e.Cancelled, i.GuestName, i.GuestContact, i.Status,
                i.ConvertedUserId, i.InviterId, EventAccess.Display(u.FirstName, u.LastName, u.Nickname), u.GroupId,
                u.GroupId is { } gid ? groups.GetValueOrDefault(gid) : null, i.InviterId == me.AppUserId,
                CanConvert: leads && i.Status == InviteStatus.CAME && i.ConvertedUserId is null);
        }).ToList();
    }

    /// <summary>Newest event first, then newest logged.</summary>
    public static async Task<List<InviteDto>> Query(IAppDbContext db, CurrentUserInfo me, IQueryable<Invite> q, int take, CancellationToken ct)
    {
        var rows = await (
            from i in q.AsNoTracking()
            join e in db.Events on i.EventId equals e.Id
            orderby e.StartsAt descending, i.CreatedAt descending
            select i).Take(take).ToListAsync(ct);
        return await Dtos(db, me, rows, ct);
    }

    public static async Task<InviteDto> Dto(IAppDbContext db, CurrentUserInfo me, Invite i, CancellationToken ct) =>
        (await Dtos(db, me, [i], ct))[0];

    /// <summary>Guests have no account, so their inviters are told, to pass it on (§6.6).</summary>
    public static async Task NotifyCancelled(IAppDbContext db, Event e, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var inviters = await db.Invites.Where(i => i.EventId == e.Id && i.Status == InviteStatus.EXPECTED && i.InviterId != actorId)
            .GroupBy(i => i.InviterId).Select(g => new { UserId = g.Key, Guests = g.Count() }).ToListAsync(ct);
        foreach (var x in inviters)
        {
            var payload = JsonSerializer.Serialize(new { eventId = e.Id, title = e.Title, startsAt = e.StartsAt, guests = x.Guests }, Scheduling.Json);
            db.Notifications.Add(Notification.Create(e.ChurchId, x.UserId, "invite.event_cancelled", payload, ["in_app", "email"], now));
        }
    }
}

// ---------------------------------------------------------------- Read

/// <summary>The caller's invites (last 60 days and upcoming) and the events they can invite a guest to.</summary>
public sealed record GetMyInvites : IRequest<MyInvitesDto>;

public sealed class GetMyInvitesHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<GetMyInvites, MyInvitesDto>
{
    public async Task<MyInvitesDto> Handle(GetMyInvites r, CancellationToken ct)
    {
        var me = current.Info;
        var (_, today, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        var (first, last) = (today.AddDays(-InviteAccess.LogDaysBack), today.AddDays(InviteAccess.LogDaysAhead));
        var events = await (
            from e in EventAccess.VisibleTo(db, me)
            join t in db.EventTypes on e.EventTypeId equals t.Id
            where InviteAccess.EventTypes.Contains(t.Code) && e.CancelledAt == null && e.OccursOn >= first && e.OccursOn <= last
            orderby e.StartsAt
            select new InviteEventDto(e.Id, e.Title, t.Code, e.StartsAt)).Take(50).ToListAsync(ct);
        var since = today.AddDays(-60);
        var mine = db.Invites.Where(i => i.InviterId == me.AppUserId && db.Events.Any(e => e.Id == i.EventId && e.OccursOn >= since));
        return new MyInvitesDto(events, await InviteAccess.Query(db, me, mine, 200, ct));
    }
}

/// <summary>A leader's view: invites by the members of their groups (admins: everyone), for one event or a date range.</summary>
public sealed record ListInvites(Guid? EventId, DateOnly? From, DateOnly? To) : IRequest<IReadOnlyList<InviteDto>>;

public sealed class ListInvitesHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<ListInvites, IReadOnlyList<InviteDto>>
{
    public async Task<IReadOnlyList<InviteDto>> Handle(ListInvites r, CancellationToken ct)
    {
        var me = current.Info;
        var q = InviteAccess.Reachable(db, me);
        if (r.EventId is { } eventId) q = q.Where(i => i.EventId == eventId);
        else
        {
            var (_, today, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
            var (from, to) = (r.From ?? today.AddDays(-30), r.To ?? today.AddDays(InviteAccess.LogDaysAhead));
            if (to < from || to.DayNumber - from.DayNumber > 92) throw new DomainException("invite.range", "Pick a range of up to 92 days.");
            q = q.Where(i => db.Events.Any(e => e.Id == i.EventId && e.OccursOn >= from && e.OccursOn <= to));
        }
        return await InviteAccess.Query(db, me, q, 500, ct); // shortcut: capped at 500 rows; page it if a church outgrows that
    }
}

// ---------------------------------------------------------------- Write

public sealed record LogInvite(Guid EventId, string GuestName, string? GuestContact) : IRequest<InviteDto>, IAuditedRequest
{
    public string AuditAction => "invite.log";
}

public sealed class LogInviteValidator : AbstractValidator<LogInvite>
{
    public LogInviteValidator()
    {
        RuleFor(x => x.GuestName).NotEmpty().MaximumLength(Invite.MaxNameLength);
        RuleFor(x => x.GuestContact).MaximumLength(Invite.MaxContactLength);
    }
}

public sealed class LogInviteHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock) : IRequestHandler<LogInvite, InviteDto>
{
    public async Task<InviteDto> Handle(LogInvite r, CancellationToken ct)
    {
        var me = current.Info;
        var e = await EventAccess.LoadVisible(db, me, r.EventId, ct);
        var code = await db.EventTypes.Where(t => t.Id == e.EventTypeId).Select(t => t.Code).FirstAsync(ct);
        if (!InviteAccess.EventTypes.Contains(code))
            throw new DomainException("invite.event_type", "Guests can be invited to a Cell Meeting or Cell Celebration.");
        if (e.CancelledAt is not null) throw new DomainException("invite.event_cancelled", "This event was cancelled.");
        var (_, today, _) = await EventAccess.ChurchClock(db, me.ChurchId, clock, ct);
        if (e.OccursOn < today.AddDays(-InviteAccess.LogDaysBack))
            throw new DomainException("invite.too_late", "Guests can be logged up to a week after the event.");
        var settings = await db.Churches.Where(c => c.Id == me.ChurchId).Select(c => c.Settings).FirstAsync(ct);

        var invite = Invite.Log(me.ChurchId, me.AppUserId, e.Id, r.GuestName, r.GuestContact,
            e.OccursOn.AddDays(InviteAccess.PurgeAfterDays(settings)));
        db.Invites.Add(invite);
        // Guest details stay out of the audit log; it records only whether a contact was given.
        audit.Describe("invite", invite.Id, null, new { eventId = e.Id, hasContact = invite.GuestContact is not null });
        return await InviteAccess.Dto(db, me, invite, ct);
    }
}

public sealed record EditInvite(Guid Id, string GuestName, string? GuestContact) : IRequest<InviteDto>, IAuditedRequest
{
    public string AuditAction => "invite.edit";
}

public sealed class EditInviteValidator : AbstractValidator<EditInvite>
{
    public EditInviteValidator()
    {
        RuleFor(x => x.GuestName).NotEmpty().MaximumLength(Invite.MaxNameLength);
        RuleFor(x => x.GuestContact).MaximumLength(Invite.MaxContactLength);
    }
}

public sealed class EditInviteHandler(IAppDbContext db, ICurrentUser current, AuditScope audit) : IRequestHandler<EditInvite, InviteDto>
{
    public async Task<InviteDto> Handle(EditInvite r, CancellationToken ct)
    {
        var me = current.Info;
        var invite = await InviteAccess.Load(db, me, r.Id, ct);
        var hadContact = invite.GuestContact is not null;
        invite.Edit(r.GuestName, r.GuestContact);
        audit.Describe("invite", invite.Id, new { hasContact = hadContact }, new { hasContact = invite.GuestContact is not null });
        return await InviteAccess.Dto(db, me, invite, ct);
    }
}

/// <summary>EXPECTED → CAME / DID_NOT_COME, by the inviter or their leader (§6.5), once the event has started.</summary>
public sealed record MarkInvite(Guid Id, InviteStatus Status) : IRequest<InviteDto>, IAuditedRequest
{
    public string AuditAction => "invite.mark";
}

public sealed class MarkInviteValidator : AbstractValidator<MarkInvite>
{
    public MarkInviteValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class MarkInviteHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock) : IRequestHandler<MarkInvite, InviteDto>
{
    public async Task<InviteDto> Handle(MarkInvite r, CancellationToken ct)
    {
        var me = current.Info;
        var invite = await InviteAccess.Load(db, me, r.Id, ct);
        var startsAt = await db.Events.Where(e => e.Id == invite.EventId).Select(e => e.StartsAt).FirstAsync(ct);
        if (r.Status != InviteStatus.EXPECTED && startsAt > clock.GetUtcNow())
            throw new DomainException("invite.too_early", "You can mark this once the event has started.");
        var before = invite.Status;
        invite.Mark(r.Status);
        audit.Describe("invite", invite.Id, new { status = before }, new { status = invite.Status });
        return await InviteAccess.Dto(db, me, invite, ct);
    }
}

/// <summary>Removes an invite logged by mistake and erases the guest's details. A converted invite stays: it's the member's history.</summary>
public sealed record RemoveInvite(Guid Id) : IRequest<Guid>, IAuditedRequest
{
    public string AuditAction => "invite.remove";
}

public sealed class RemoveInviteHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, TimeProvider clock) : IRequestHandler<RemoveInvite, Guid>
{
    public async Task<Guid> Handle(RemoveInvite r, CancellationToken ct)
    {
        var invite = await InviteAccess.Load(db, current.Info, r.Id, ct);
        invite.Erase(clock.GetUtcNow());
        audit.Describe("invite", invite.Id, new { removed = false }, new { removed = true });
        return invite.Id;
    }
}
