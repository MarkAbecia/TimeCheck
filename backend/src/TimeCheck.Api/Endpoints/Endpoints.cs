using TimeCheck.Api.Auth;
using TimeCheck.Application.Abstractions;
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
        groups.MapGet("/{groupId:guid}", (Guid groupId, Dispatcher d, CancellationToken ct) => d.Send(new GetGroup(groupId), ct));

        var admin = v1.MapGroup("/admin").WithTags("Admin");
        admin.MapGet("/audit-logs", (Guid? actorId, string? action, string? entityType, DateTimeOffset? from, DateTimeOffset? to,
                int? limit, string? cursor, Dispatcher d, CancellationToken ct) =>
                d.Send(new ListAuditLogs(actorId, action, entityType, from, to, limit ?? 50, cursor), ct))
            .RequireAuthorization(Policies.SuperAdmin);
    }

    private static Task InvalidateCachedUser(HttpContext http, IUserDirectory users, CancellationToken ct) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out var authUserId) ? users.InvalidateAsync(authUserId, ct) : Task.CompletedTask;
}
