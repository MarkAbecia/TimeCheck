using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Entities;

namespace TimeCheck.Application.Features.Notifications;

// ---------------------------------------------------------------- What each notification says (§6.7)

public sealed record RenderedNotification(string Title, string Body, string Url);

/// <summary>
/// Every notification type, its preference category and its words. One place, so in-app, email and push say the same
/// thing. Payloads are the outbox rows' JSON; times are shown in church time.
/// </summary>
public static class NotificationCatalog
{
    public const string Email = "email";
    public const string Push = "push";
    public const string InApp = "in_app";

    /// <summary>Categories people choose channels for, with what's on until they change it.</summary>
    public static readonly IReadOnlyDictionary<string, (bool Email, bool Push)> Defaults = new Dictionary<string, (bool, bool)>
    {
        ["account"] = (true, true),
        ["events"] = (true, true),
        ["attendance"] = (true, true),
        ["devotions"] = (false, true),
        ["invites"] = (false, true),
    };

    public static string Category(string type) => type switch
    {
        "role.changed" => "account",
        "attendance.unmarked" => "attendance",
        "invite.event_cancelled" => "events",
        _ when type.StartsWith("event.", StringComparison.Ordinal) => "events",
        _ when type.StartsWith("devotion.", StringComparison.Ordinal) => "devotions",
        _ when type.StartsWith("invite.", StringComparison.Ordinal) => "invites",
        _ => "account",
    };

    public static RenderedNotification Render(string type, string payload, TimeZoneInfo tz)
    {
        using var doc = JsonDocument.Parse(payload);
        var p = doc.RootElement;
        string S(string name) => p.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";
        int N(string name) => p.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
        string When() => p.TryGetProperty("startsAt", out var v) && v.TryGetDateTimeOffset(out var at) ? Format(at, tz) : "";
        string Plural(int n, string one) => n == 1 ? $"1 {one}" : $"{n} {one}s";
        var eventUrl = $"/events/{S("eventId")}";

        return type switch
        {
            "role.changed" => new("Your role changed", $"You're now {Role(S("to"))} in TimeCheck.", "/"),
            "mfa.reset" => new("Your sign-in code was reset",
                "Set up a new one the next time you sign in. If you didn't ask for this, tell your church admin.", "/profile"),
            "event.scheduled" => new($"New: {S("title")}", $"{When()}.", eventUrl),
            "event.changed" => new($"Changed: {S("title")}", $"Now {When()}.", eventUrl),
            "event.cancelled" => new($"Cancelled: {S("title")}", $"{When()} is off.", eventUrl),
            "invite.event_cancelled" => new($"Tell your guests: {S("title")} is cancelled",
                $"You invited {Plural(N("guests"), "guest")} to {When()}.", "/invites"),
            "invite.reminder" => new("Did your guest come?", $"Mark whether your {Plural(N("guests"), "guest")} came.", "/invites"),
            "devotion.reminder.day" => new("Day devotion", "You haven't sent today's Day devotion yet.", "/devotions"),
            "devotion.reminder.night" => new("Night devotion", "There's still time for tonight's devotion.", "/devotions"),
            "report.ready" => new($"Ready: {S("title")} ({S("format")})", "Your export is ready to download for 24 hours.", "/reports/exports"),
            "attendance.unmarked" => new($"Not marked yet: {S("title")}",
                $"{Plural(N("members"), "member")} still unmarked from {When()}.", eventUrl),
            _ => new("TimeCheck", "You have a new notification.", "/"),
        };
    }

    /// <summary>"Sun 29 Nov, 9 AM" in church time.</summary>
    public static string Format(DateTimeOffset at, TimeZoneInfo tz)
    {
        var l = TimeZoneInfo.ConvertTime(at, tz);
        var time = l.ToString(l.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture);
        return $"{l.ToString("ddd d MMM", CultureInfo.InvariantCulture)}, {time}";
    }

    private static string Role(string role) => role switch
    {
        "SUPER_ADMIN" => "a super admin",
        "ADMIN" => "an admin",
        "LEADER" => "a leader",
        _ => "a member",
    };
}

// ---------------------------------------------------------------- Preferences

public sealed record CategoryPreferenceDto(string Category, bool Email, bool Push);

/// <summary>In-app is always on: the list is where every notification lands.</summary>
public sealed record NotificationPreferencesDto(
    IReadOnlyList<CategoryPreferenceDto> Categories, bool EmailAvailable, bool PushAvailable, string? PushPublicKey, int PushDevices);

public static class NotificationPreferences
{
    /// <summary>The person's choices (app_users.settings.notifications), with defaults for anything unset.</summary>
    public static Dictionary<string, (bool Email, bool Push)> Read(string settings)
    {
        var saved = JsonNode.Parse(settings)?["notifications"] as JsonObject;
        return NotificationCatalog.Defaults.ToDictionary(d => d.Key, d =>
        {
            var c = saved?[d.Key] as JsonObject;
            return (c?["email"]?.GetValue<bool>() ?? d.Value.Email, c?["push"]?.GetValue<bool>() ?? d.Value.Push);
        });
    }

    public static string Write(string settings, IEnumerable<CategoryPreferenceDto> prefs)
    {
        var root = JsonNode.Parse(settings) as JsonObject ?? [];
        var obj = new JsonObject();
        foreach (var p in prefs) obj[p.Category] = new JsonObject { ["email"] = p.Email, ["push"] = p.Push };
        root["notifications"] = obj;
        return root.ToJsonString();
    }
}

public sealed record GetNotificationPreferences : IRequest<NotificationPreferencesDto>;

public sealed class GetNotificationPreferencesHandler(IAppDbContext db, ICurrentUser current, IEmailSender email, IPushSender push)
    : IRequestHandler<GetNotificationPreferences, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> Handle(GetNotificationPreferences r, CancellationToken ct)
    {
        var me = current.Info;
        var settings = await db.Users.Where(u => u.Id == me.AppUserId).Select(u => u.Settings).FirstAsync(ct);
        var devices = await db.PushSubscriptions.CountAsync(s => s.UserId == me.AppUserId, ct);
        var prefs = NotificationPreferences.Read(settings);
        return new([.. prefs.Select(p => new CategoryPreferenceDto(p.Key, p.Value.Email, p.Value.Push))],
            email.Enabled, push.Enabled, push.PublicKey, devices);
    }
}

public sealed record UpdateNotificationPreferences(IReadOnlyList<CategoryPreferenceDto> Categories) : IRequest<Guid>, IAuditedRequest
{
    public string AuditAction => "notification.preferences";
}

public sealed class UpdateNotificationPreferencesValidator : AbstractValidator<UpdateNotificationPreferences>
{
    public UpdateNotificationPreferencesValidator()
    {
        RuleFor(x => x.Categories).NotNull();
        RuleForEach(x => x.Categories).Must(c => NotificationCatalog.Defaults.ContainsKey(c.Category)).WithMessage("Unknown category.");
        RuleFor(x => x.Categories).Must(c => c.Select(x => x.Category).Distinct().Count() == c.Count).WithMessage("Each category once.");
    }
}

public sealed class UpdateNotificationPreferencesHandler(IAppDbContext db, ICurrentUser current, AuditScope audit)
    : IRequestHandler<UpdateNotificationPreferences, Guid>
{
    public async Task<Guid> Handle(UpdateNotificationPreferences r, CancellationToken ct)
    {
        var me = current.Info;
        var user = await db.Users.FirstAsync(u => u.Id == me.AppUserId, ct);
        var before = NotificationPreferences.Read(user.Settings);
        // Categories left out keep their current choice.
        var merged = before.ToDictionary(b => b.Key, b => new CategoryPreferenceDto(b.Key, b.Value.Email, b.Value.Push));
        foreach (var c in r.Categories) merged[c.Category] = c;
        user.ReplaceSettings(NotificationPreferences.Write(user.Settings, merged.Values));
        audit.Describe("app_user", user.Id, Shape(before.Select(b => new CategoryPreferenceDto(b.Key, b.Value.Email, b.Value.Push))), Shape(merged.Values));
        return user.Id;
    }

    private static Dictionary<string, string> Shape(IEnumerable<CategoryPreferenceDto> p) =>
        p.ToDictionary(c => c.Category, c => $"{(c.Email ? "email" : "")}{(c.Email && c.Push ? "+" : "")}{(c.Push ? "push" : "")}");
}

// ---------------------------------------------------------------- In-app list

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, string Url, DateTimeOffset CreatedAt, bool Read);

public sealed record NotificationListDto(IReadOnlyList<NotificationDto> Items, int Unread, string? NextCursor);

public sealed record ListMyNotifications(string? Cursor, int Limit = 30) : IRequest<NotificationListDto>;

public sealed class ListMyNotificationsHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<ListMyNotifications, NotificationListDto>
{
    public async Task<NotificationListDto> Handle(ListMyNotifications r, CancellationToken ct)
    {
        var me = current.Info;
        var limit = Math.Clamp(r.Limit, 1, 100);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me.AppUserId);
        var unread = await q.CountAsync(n => n.ReadAt == null, ct);
        if (Cursor.Decode(r.Cursor) is { } c)
            q = q.Where(n => n.CreatedAt < c.At || (n.CreatedAt == c.At && n.Id.CompareTo(c.Id) < 0));
        var rows = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(limit + 1).ToListAsync(ct);
        var tz = (await db.Churches.AsNoTracking().FirstAsync(c => c.Id == me.ChurchId, ct)).GetTimeZone();
        var items = rows.Take(limit).Select(n =>
        {
            var text = NotificationCatalog.Render(n.Type, n.Payload, tz);
            return new NotificationDto(n.Id, n.Type, text.Title, text.Body, text.Url, n.CreatedAt, n.ReadAt is not null);
        }).ToList();
        return new(items, unread, rows.Count > limit ? Cursor.Encode(rows[limit - 1].CreatedAt, rows[limit - 1].Id) : null);
    }
}

/// <summary>Marks some (or, with no ids, all) of the caller's notifications read. Others' ids are ignored.</summary>
public sealed record MarkNotificationsRead(IReadOnlyList<Guid>? Ids) : IRequest<int>;

public sealed class MarkNotificationsReadHandler(IAppDbContext db, ICurrentUser current, TimeProvider clock) : IRequestHandler<MarkNotificationsRead, int>
{
    public async Task<int> Handle(MarkNotificationsRead r, CancellationToken ct)
    {
        var me = current.Info;
        var q = db.Notifications.Where(n => n.UserId == me.AppUserId && n.ReadAt == null);
        if (r.Ids is { Count: > 0 } ids) q = q.Where(n => ids.Contains(n.Id));
        var rows = await q.Take(500).ToListAsync(ct);
        foreach (var n in rows) n.MarkRead(clock.GetUtcNow());
        return rows.Count;
    }
}

// ---------------------------------------------------------------- Push subscriptions

public sealed record SavePushSubscription(string Endpoint, string P256dh, string Auth) : IRequest<Guid>;

public sealed class SavePushSubscriptionValidator : AbstractValidator<SavePushSubscription>
{
    public SavePushSubscriptionValidator()
    {
        // Push services are https; anything else would make the Worker call an arbitrary URL.
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(1000)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps).WithMessage("Not a push endpoint.");
        RuleFor(x => x.P256dh).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Auth).NotEmpty().MaximumLength(100);
    }
}

public sealed class SavePushSubscriptionHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<SavePushSubscription, Guid>
{
    public async Task<Guid> Handle(SavePushSubscription r, CancellationToken ct)
    {
        var me = current.Info;
        // Endpoints are unique across churches; a shared phone moves to whoever signed in last.
        var existing = await db.PushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Endpoint == r.Endpoint, ct);
        if (existing is not null && existing.ChurchId != me.ChurchId)
        {
            db.PushSubscriptions.Remove(existing);
            existing = null;
        }
        if (existing is null)
        {
            existing = PushSubscription.Create(me.ChurchId, me.AppUserId, r.Endpoint, r.P256dh, r.Auth);
            db.PushSubscriptions.Add(existing);
        }
        else existing.Reassign(me.AppUserId, r.P256dh, r.Auth);
        return existing.Id;
    }
}

/// <summary>This browser stops getting pushes (turned off, or signing out).</summary>
public sealed record RemovePushSubscription(string Endpoint) : IRequest<int>;

public sealed class RemovePushSubscriptionHandler(IAppDbContext db, ICurrentUser current) : IRequestHandler<RemovePushSubscription, int>
{
    public async Task<int> Handle(RemovePushSubscription r, CancellationToken ct)
    {
        var rows = await db.PushSubscriptions.Where(s => s.UserId == current.Info.AppUserId && s.Endpoint == r.Endpoint).ToListAsync(ct);
        db.PushSubscriptions.RemoveRange(rows);
        return rows.Count;
    }
}
