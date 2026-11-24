using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Members;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Features.Groups;

public sealed record GroupSummaryDto(Guid Id, string Name, Guid? LeaderId, string? LeaderName, int MemberCount);
public sealed record LeaderOptionDto(Guid Id, string DisplayName, UserRole Role);

// shortcut: groups and leader options are returned whole, not paged; a church has tens of groups. Page if that grows past ~500.

/// <summary>Admins see every active group; leaders see the groups they lead.</summary>
public sealed record ListGroups : IRequest<IReadOnlyList<GroupSummaryDto>>;

public sealed class ListGroupsHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<ListGroups, IReadOnlyList<GroupSummaryDto>>
{
    public async Task<IReadOnlyList<GroupSummaryDto>> Handle(ListGroups request, CancellationToken ct)
    {
        var me = current.Info;
        var admin = me.IsAdmin();
        var led = me.LedGroupIds.ToList();
        var rows = await db.Groups.AsNoTracking()
            .Where(g => g.ArchivedAt == null && (admin || led.Contains(g.Id)))
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id, g.Name, g.LeaderId,
                Leader = db.Users.Where(u => u.Id == g.LeaderId).Select(u => new { u.FirstName, u.LastName, u.Nickname }).FirstOrDefault(),
                Count = db.GroupMemberships.Count(m => m.GroupId == g.Id && m.LeftOn == null),
            })
            .ToListAsync(ct);
        return rows.Select(g => new GroupSummaryDto(g.Id, g.Name, g.LeaderId,
            g.Leader is null ? null : string.IsNullOrWhiteSpace(g.Leader.Nickname) ? $"{g.Leader.FirstName} {g.Leader.LastName}" : g.Leader.Nickname,
            g.Count)).ToList();
    }
}

/// <summary>People who can be assigned to lead a group (role Leader or above, active).</summary>
public sealed record ListLeaderOptions : IRequest<IReadOnlyList<LeaderOptionDto>>;

public sealed class ListLeaderOptionsHandler(IAppDbContext db) : IRequestHandler<ListLeaderOptions, IReadOnlyList<LeaderOptionDto>>
{
    public async Task<IReadOnlyList<LeaderOptionDto>> Handle(ListLeaderOptions request, CancellationToken ct)
    {
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.Role != UserRole.MEMBER && u.DeactivatedAt == null)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Nickname, u.Role })
            .ToListAsync(ct);
        return rows.Select(u => new LeaderOptionDto(u.Id,
            string.IsNullOrWhiteSpace(u.Nickname) ? $"{u.FirstName} {u.LastName}" : u.Nickname, u.Role)).ToList();
    }
}

internal static class GroupRules
{
    public static async Task CheckName(IAppDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        // Case-insensitive, like the unique index on lower(name), which stays the final guard. A church has tens of groups.
        var names = await db.Groups.AsNoTracking().Where(g => g.ArchivedAt == null && g.Id != exceptId).Select(g => g.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("Another group already has this name.");
    }

    /// <summary>Returns the leader's auth id (for cache eviction) after checking they can lead.</summary>
    public static async Task<Guid?> CheckLeader(IAppDbContext db, Guid? leaderId, CancellationToken ct)
    {
        if (leaderId is null) return null;
        var leader = await db.Users.AsNoTracking().Where(u => u.Id == leaderId)
            .Select(u => new { u.Role, u.DeactivatedAt, u.AuthUserId }).FirstOrDefaultAsync(ct);
        if (leader is null || leader.DeactivatedAt is not null || !leader.Role.IsAtLeast(UserRole.LEADER))
            throw new ValidationException([new ValidationFailure("leaderId", "Choose someone with the Leader role. Promote them first if needed.")]);
        return leader.AuthUserId;
    }

    public static Task<Guid?> AuthIdOf(IAppDbContext db, Guid? userId, CancellationToken ct) => userId is null
        ? Task.FromResult<Guid?>(null)
        : db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.AuthUserId).FirstOrDefaultAsync(ct);
}

public sealed record CreateGroup(string Name, Guid? LeaderId) : IRequest<GroupSummaryDto>, IAuditedRequest
{
    public string AuditAction => "group.create";
}

public sealed class CreateGroupValidator : AbstractValidator<CreateGroup>
{
    public CreateGroupValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class CreateGroupHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users)
    : IRequestHandler<CreateGroup, GroupSummaryDto>
{
    public async Task<GroupSummaryDto> Handle(CreateGroup r, CancellationToken ct)
    {
        await GroupRules.CheckName(db, r.Name, null, ct);
        var leaderAuth = await GroupRules.CheckLeader(db, r.LeaderId, ct);
        var group = CellGroup.Create(current.Info.ChurchId, r.Name, r.LeaderId, null, current.Info.AppUserId);
        db.Groups.Add(group);
        Members.Members.EvictCache(uow, users, leaderAuth); // the leader's led groups are cached
        audit.Describe("cell_group", group.Id, null, new { group.Name, group.LeaderId });
        return new GroupSummaryDto(group.Id, group.Name, group.LeaderId, null, 0);
    }
}

public sealed record UpdateGroup(Guid Id, string Name, Guid? LeaderId) : IRequest<GroupSummaryDto>, IAuditedRequest
{
    public string AuditAction => "group.update";
}

public sealed class UpdateGroupValidator : AbstractValidator<UpdateGroup>
{
    public UpdateGroupValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class UpdateGroupHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users)
    : IRequestHandler<UpdateGroup, GroupSummaryDto>
{
    public async Task<GroupSummaryDto> Handle(UpdateGroup r, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == r.Id && g.ArchivedAt == null, ct) ?? throw new NotFoundException();
        var before = new { group.Name, group.LeaderId };
        await GroupRules.CheckName(db, r.Name, group.Id, ct);

        if (group.LeaderId != r.LeaderId)
        {
            Members.Members.EvictCache(uow, users, await GroupRules.AuthIdOf(db, group.LeaderId, ct));
            Members.Members.EvictCache(uow, users, await GroupRules.CheckLeader(db, r.LeaderId, ct));
            group.AssignLeader(r.LeaderId, current.Info.AppUserId);
        }
        if (group.Name != r.Name.Trim()) group.Rename(r.Name, current.Info.AppUserId);

        audit.Describe("cell_group", group.Id, before, new { group.Name, group.LeaderId });
        var count = await db.GroupMemberships.CountAsync(m => m.GroupId == group.Id && m.LeftOn == null, ct);
        return new GroupSummaryDto(group.Id, group.Name, group.LeaderId, null, count);
    }
}

public sealed record ArchiveGroup(Guid Id) : IRequest<bool>, IAuditedRequest
{
    public string AuditAction => "group.archive";
}

public sealed class ArchiveGroupHandler(IAppDbContext db, ICurrentUser current, AuditScope audit, UnitOfWork uow, IUserDirectory users, TimeProvider clock)
    : IRequestHandler<ArchiveGroup, bool>
{
    public async Task<bool> Handle(ArchiveGroup r, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == r.Id && g.ArchivedAt == null, ct) ?? throw new NotFoundException();
        if (await db.GroupMemberships.AnyAsync(m => m.GroupId == group.Id && m.LeftOn == null, ct))
            throw new ConflictException("Move this group's members to another group first.");
        Members.Members.EvictCache(uow, users, await GroupRules.AuthIdOf(db, group.LeaderId, ct));
        group.Archive(clock.GetUtcNow(), current.Info.AppUserId);
        audit.Describe("cell_group", group.Id, null, new { archived = true });
        return true;
    }
}
