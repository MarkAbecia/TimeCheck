using TimeCheck.Api.Auth;
using TimeCheck.Api.Infrastructure;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Admin;
using TimeCheck.Application.Features.Devotions;
using TimeCheck.Application.Features.Invites;
using TimeCheck.Application.Features.Notifications;
using TimeCheck.Application.Features.Events;
using TimeCheck.Application.Features.Groups;
using TimeCheck.Application.Features.Me;
using TimeCheck.Application.Features.Members;
using TimeCheck.Application.Features.Reports;
using TimeCheck.Domain.Enums;

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
        // Today and history are app pages, so they sit behind the onboarding gate (the fallback policy), not just SignedIn.
        v1.MapGet("/me/today", (Dispatcher d, CancellationToken ct) => d.Send(new GetMyToday(), ct)).WithTags("Me");
        v1.MapGet("/me/attendance", (DateOnly? from, DateOnly? to, Dispatcher d, CancellationToken ct) => d.Send(new GetMyAttendance(from, to), ct)).WithTags("Me");
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

        v1.MapGet("/event-types", (Dispatcher d, CancellationToken ct) => d.Send(new ListEventTypes(), ct)).WithTags("Events");

        var events = v1.MapGroup("/events").WithTags("Events");
        events.MapGet("", (DateOnly from, DateOnly to, Guid? groupId, Guid? typeId, EventCategory? category, bool? includeCancelled,
            Dispatcher d, CancellationToken ct) => d.Send(new ListEvents(from, to, groupId, typeId, category, includeCancelled ?? false), ct));
        events.MapGet("/{id:guid}", (Guid id, Guid? groupId, Dispatcher d, CancellationToken ct) => d.Send(new GetEvent(id, groupId), ct));
        events.MapPost("", async (CreateEvent body, Dispatcher d, CancellationToken ct) =>
        {
            var id = await d.Send(body, ct);
            return Results.Created($"/api/v1/events/{id}", await d.Send(new GetEvent(id), ct));
        }).RequireAuthorization(Policies.Leader);
        events.MapPatch("/{id:guid}", (Guid id, UpdateEvent body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct))
            .RequireAuthorization(Policies.Leader);
        events.MapPost("/{id:guid}/cancel", (Guid id, Dispatcher d, CancellationToken ct) => d.Send(new CancelEvent(id), ct))
            .RequireAuthorization(Policies.Leader);
        // PUT: resending the same marks is harmless, so phones can retry (§9). The response is the saved roster.
        events.MapPut("/{id:guid}/attendance", async (Guid id, Guid? groupId, MarkAttendance body, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(body with { EventId = id }, ct);
            return await d.Send(new GetEvent(id, groupId), ct);
        }).RequireAuthorization(Policies.Leader);

        // Devotions (§6.3). Uploading is PUT on the slot: a retry or a second upload edits the same devotion (§9 idempotency).
        v1.MapGet("/me/devotions", (Dispatcher d, CancellationToken ct) => d.Send(new GetMyDevotions(), ct)).WithTags("Devotions");
        v1.MapPut("/me/devotions/{date}/{slot}", async (DateOnly date, DevotionSlot slot, HttpRequest req, Dispatcher d, CancellationToken ct) =>
            {
                // multipart/form-data: text, keepImage, and an optional image file. Read by hand so the size cap applies first.
                var form = await req.ReadFormAsync(ct);
                byte[]? image = null;
                if (form.Files.GetFile("image") is { Length: > 0 } file)
                {
                    if (file.Length > SubmitDevotion.MaxImageBytes) return Results.Problem(statusCode: 413, title: "Images can be up to 10 MB.");
                    using var ms = new MemoryStream((int)file.Length);
                    await file.CopyToAsync(ms, ct);
                    image = ms.ToArray();
                }
                var keep = bool.TryParse(form["keepImage"], out var k) && k;
                return Results.Ok(await d.Send(new SubmitDevotion(date, slot, form["text"], image, keep), ct));
            })
            .WithTags("Devotions")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(SubmitDevotion.MaxImageBytes + 64 * 1024))
            .WithRateLimit(RateLimits.Uploads);
        var devotions = v1.MapGroup("/devotions").WithTags("Devotions");
        devotions.MapGet("", (DateOnly? date, Guid? groupId, bool? likelyAi, Dispatcher d, CancellationToken ct) =>
                d.Send(new ListGroupDevotions(date, groupId, likelyAi ?? false), ct))
            .RequireAuthorization(Policies.Leader);
        // Owner, their leader, or an admin; anyone else gets 404. Includes a 5-minute signed image URL.
        devotions.MapGet("/{id:guid}", (Guid id, Dispatcher d, CancellationToken ct) => d.Send(new GetDevotion(id), ct));
        devotions.MapPost("/{id:guid}/review", async (Guid id, ReviewDevotion body, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(body with { Id = id }, ct);
            return await d.Send(new GetDevotion(id), ct);
        }).RequireAuthorization(Policies.Leader);

        // Invites (§6.5). Any signed-in member logs their own guests; leaders see and mark their groups' (others get 404).
        v1.MapGet("/me/invites", (Dispatcher d, CancellationToken ct) => d.Send(new GetMyInvites(), ct)).WithTags("Invites");
        var invites = v1.MapGroup("/invites").WithTags("Invites");
        invites.MapGet("", (Guid? eventId, DateOnly? from, DateOnly? to, Dispatcher d, CancellationToken ct) =>
            d.Send(new ListInvites(eventId, from, to), ct)).RequireAuthorization(Policies.Leader);
        invites.MapPost("", async (LogInvite body, Dispatcher d, CancellationToken ct) =>
        {
            var invite = await d.Send(body, ct);
            return Results.Created($"/api/v1/invites/{invite.Id}", invite);
        });
        invites.MapPatch("/{id:guid}", (Guid id, EditInvite body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct));
        invites.MapPut("/{id:guid}/status", (Guid id, MarkInvite body, Dispatcher d, CancellationToken ct) => d.Send(body with { Id = id }, ct));
        invites.MapDelete("/{id:guid}", async (Guid id, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(new RemoveInvite(id), ct);
            return Results.NoContent();
        });

        // Notifications (§6.7): the in-app list, channel choices, and this browser's push subscription.
        var notifications = v1.MapGroup("/me").WithTags("Notifications");
        notifications.MapGet("/notifications", (string? cursor, int? limit, Dispatcher d, CancellationToken ct) =>
            d.Send(new ListMyNotifications(cursor, limit ?? 30), ct));
        notifications.MapPost("/notifications/read", async (MarkNotificationsRead body, Dispatcher d, CancellationToken ct) =>
            Results.Ok(new { marked = await d.Send(body, ct) }));
        notifications.MapGet("/notification-preferences", (Dispatcher d, CancellationToken ct) => d.Send(new GetNotificationPreferences(), ct));
        notifications.MapPut("/notification-preferences", async (UpdateNotificationPreferences body, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(body, ct);
            return await d.Send(new GetNotificationPreferences(), ct);
        });
        notifications.MapPost("/push-subscriptions", async (SavePushSubscription body, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(body, ct);
            return Results.NoContent();
        });
        notifications.MapPost("/push-subscriptions/remove", async (RemovePushSubscription body, Dispatcher d, CancellationToken ct) =>
        {
            await d.Send(body, ct);
            return Results.NoContent();
        });

        // Reports (§6.8). Leaders get their group's; the admin-only ones are 403 for them. Exports are rate-limited.
        var reports = v1.MapGroup("/reports").WithTags("Reports").RequireAuthorization(Policies.Leader);
        reports.MapGet("", (Dispatcher d, CancellationToken ct) => d.Send(new ListReports(), ct));
        reports.MapGet("/{type}", (string type, DateOnly? from, DateOnly? to, Guid? groupId, Guid? eventTypeId, EventCategory? category, Dispatcher d, CancellationToken ct) =>
            d.Send(new RunReport(type, new ReportFilters(from, to, groupId, eventTypeId, category)), ct));
        reports.MapPost("/{type}/exports", async (string type, ExportReport body, Dispatcher d, CancellationToken ct) =>
        {
            var job = await d.Send(body with { Type = type }, ct);
            return Results.Created($"/api/v1/me/report-jobs/{job.Id}", job);
        }).WithRateLimit(RateLimits.Exports);
        var reportJobs = v1.MapGroup("/me/report-jobs").WithTags("Reports").RequireAuthorization(Policies.Leader);
        reportJobs.MapGet("", (Dispatcher d, CancellationToken ct) => d.Send(new ListMyReportJobs(), ct));
        reportJobs.MapGet("/{id:guid}/download", (Guid id, Dispatcher d, CancellationToken ct) => d.Send(new GetReportDownload(id), ct));

        var admin = v1.MapGroup("/admin").WithTags("Admin");
        admin.MapGet("/audit-logs", (Guid? actorId, string? action, string? entityType, DateTimeOffset? from, DateTimeOffset? to,
                int? limit, string? cursor, Dispatcher d, CancellationToken ct) =>
                d.Send(new ListAuditLogs(actorId, action, entityType, from, to, limit ?? 50, cursor), ct))
            .RequireAuthorization(Policies.SuperAdmin);
    }

    private static Task InvalidateCachedUser(HttpContext http, IUserDirectory users, CancellationToken ct) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out var authUserId) ? users.InvalidateAsync(authUserId, ct) : Task.CompletedTask;
}
