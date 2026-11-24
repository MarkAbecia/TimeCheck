using TimeCheck.Api.Auth;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Admin;
using TimeCheck.Application.Features.Groups;
using TimeCheck.Application.Features.Me;

namespace TimeCheck.Api.Endpoints;

public static class Endpoints
{
    public static void MapTimeCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");

        var me = v1.MapGroup("/me").WithTags("Me");
        me.MapGet("", (Dispatcher d, CancellationToken ct) => d.Send(new GetMe(), ct));
        me.MapPatch("", (UpdateMyProfile body, Dispatcher d, CancellationToken ct) => d.Send(body, ct));

        var groups = v1.MapGroup("/groups").WithTags("Groups");
        groups.MapGet("/{groupId:guid}", (Guid groupId, Dispatcher d, CancellationToken ct) => d.Send(new GetGroup(groupId), ct));

        var admin = v1.MapGroup("/admin").WithTags("Admin");
        admin.MapGet("/audit-logs", (Guid? actorId, string? action, string? entityType, DateTimeOffset? from, DateTimeOffset? to,
                int? limit, string? cursor, Dispatcher d, CancellationToken ct) =>
                d.Send(new ListAuditLogs(actorId, action, entityType, from, to, limit ?? 50, cursor), ct))
            .RequireAuthorization(Policies.SuperAdmin);
    }
}
