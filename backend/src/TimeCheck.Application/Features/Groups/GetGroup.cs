using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;

namespace TimeCheck.Application.Features.Groups;

public sealed record GroupMemberDto(Guid Id, string DisplayName, string Status, bool IsLeader);
public sealed record GroupDto(Guid Id, string Name, Guid? LeaderId, Guid? ParentGroupId, IReadOnlyList<GroupMemberDto> Members);

public sealed record GetGroup(Guid GroupId) : IRequest<GroupDto>;

public sealed class GetGroupHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<GetGroup, GroupDto>
{
    public async Task<GroupDto> Handle(GetGroup request, CancellationToken ct)
    {
        // 404 (not 403) for groups you can't see, so ids can't be probed.
        if (!current.Info.CanViewGroup(request.GroupId)) throw new NotFoundException();

        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == request.GroupId && g.ArchivedAt == null)
            .Select(g => new { g.Id, g.Name, g.LeaderId, g.ParentGroupId })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException();

        var members = await db.GroupMemberships.AsNoTracking()
            .Where(m => m.GroupId == group.Id && m.LeftOn == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.FirstName, u.LastName, u.Nickname, u.Status })
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(ct);

        return new GroupDto(group.Id, group.Name, group.LeaderId, group.ParentGroupId,
            members.Select(u => new GroupMemberDto(u.Id,
                string.IsNullOrWhiteSpace(u.Nickname) ? $"{u.FirstName} {u.LastName}" : u.Nickname,
                u.Status.ToString(), u.Id == group.LeaderId)).ToList());
    }
}
