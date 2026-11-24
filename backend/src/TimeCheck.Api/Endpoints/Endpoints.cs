using TimeCheck.Api.Auth;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Admin;
using TimeCheck.Application.Features.Groups;
using TimeCheck.Application.Features.Me;
using TimeCheck.Application.Features.Members;

namespace TimeCheck.Api.Endpoints;

public static class Endpoints
{
    public static void MapTimeCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");

        // /me works before onboarding is finished; it is how the client learns which step is owed.
        var me = v1.MapGroup("/me").WithTags("Me").RequireAuthorization(Policies.SignedIn);
        me.MapGet("", (Dispatcher d, CancellationToken ct) => d.Send(new GetMe(), ct));
        me.MapPatch("", (UpdateMyProfile body, Dispatcher d, CancellationToken ct) => d.Send(body, ct));
        me.MapPost("/password", async (ChangeMyPassword body, Dispatcher d, HttpContext http, IUserDirectory users, CancellationToken ct) =>
        {
            var result = await d.Send(body, ct);
            await InvalidateCachedUser(http, users, ct); // the onboarding gate reads the cached user
            return result;
        });
        me.MapPost("/privacy-consent", async (AcceptPrivacyNotice body, Dispatcher d, HttpContext http, IUserDirectory users, CancellationToken ct) =>
        {
            var result = await d.Send(body, ct);
            await InvalidateCachedUser(http, users, ct);
            return result;
        });

        var groups = v1.MapGroup("/groups").WithTags("Groups");
        groups.MapGet("", (Dispatcher d, CancellationToken ct) => d.Send(new ListGroups(), ct)).RequireAuthorization(Policies.Leader);
        groups.MapGet("/{groupId:guid}", (Guid groupId, Dispatcher d, CancellationToken ct) => d.Send(new GetGroup(groupId), ct));
        groups.MapGet("/leader-options", (Dispatcher d, CancellationToken ct) => d.Send(new ListLeaderOptions(), ct)).RequireAuthorization(Policies.Admin);
        groups.MapPost("", async (CreateGroup body, Dispatcher d, CancellationToken ct) =>
        {
            var group = await d.Send(body, ct);
            return Results.Created($"/api/v1/groups/{group.Id}", group);
        }).RequireAuthorization(Policies.Admin);
        groups.MapPatch("/{groupId:guid}", (Guid groupId, UpdateGroup body, Dispatcher d, CancellationToken ct) =>
            d.Send(body with { Id = groupId }, ct)).RequireAuthorization(Policies.Admin);
        groups.MapPost("/{groupId:guid}/archive", async (Guid groupId, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(new ArchiveGroup(groupId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
        // Leaders register into their own group; the handler checks which group (§6.4).
        groups.MapPost("/{groupId:guid}/members", async (Guid groupId, RegisterMember body, Dispatcher d, CancellationToken ct) =>
        {
            var member = await d.Send(body with { GroupId = groupId }, ct);
            return Results.Created($"/api/v1/members/{member.Id}", member);
        }).RequireAuthorization(Policies.Leader);

        var members = v1.MapGroup("/members").WithTags("Members").RequireAuthorization(Policies.Leader);
        members.MapGet("/{id:guid}", (Guid id, Dispatcher d, CancellationToken ct) => d.Send(new GetMember(id), ct));
        members.MapPatch("/{id:guid}", (Guid id, UpdateMember body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct));
        members.MapPost("/{id:guid}/status", (Guid id, ChangeMemberStatus body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct));
        members.MapPost("/{id:guid}/role", (Guid id, ChangeMemberRole body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct))
            .RequireAuthorization(Policies.Admin);
        members.MapPost("/{id:guid}/move", (Guid id, MoveMember body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct))
            .RequireAuthorization(Policies.Admin);

        var admin = v1.MapGroup("/admin").WithTags("Admin");
        admin.MapGet("/audit-logs", (Guid? actorId, string? action, string? entityType, DateTimeOffset? from, DateTimeOffset? to,
                int? limit, string? cursor, Dispatcher d, CancellationToken ct) =>
                d.Send(new ListAuditLogs(actorId, action, entityType, from, to, limit ?? 50, cursor), ct))
            .RequireAuthorization(Policies.SuperAdmin);
    }

    private static Task InvalidateCachedUser(HttpContext http, IUserDirectory users, CancellationToken ct) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out var authUserId) ? users.InvalidateAsync(authUserId, ct) : Task.CompletedTask;
}
