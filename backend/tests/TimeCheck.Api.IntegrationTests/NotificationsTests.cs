using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Features.Notifications;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.7: the outbox delivered by the Worker to each person's chosen channels, the in-app list, and push subscriptions.</summary>
[Collection(ApiTestGroup.Name)]
public class NotificationsTests(ApiFactory factory)
{
    private static readonly Guid MemberAppId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Member2AppId = Guid.Parse("10000000-0000-0000-0000-000000000006");

    private HttpClient Member => factory.ClientFor(ApiFactory.MemberAuth);

    /// <summary>Queues an "event changed" notification for a user, as a feature would, and returns its id.</summary>
    private Task<Guid> Queue(Guid userId, string title, string type = "event.changed") => factory.Scalar<Guid>(
        "insert into notifications (church_id, user_id, type, payload) select church_id, id, " +
        $"'{type}', jsonb_build_object('eventId', gen_random_uuid(), 'title', '{title}', 'startsAt', '2026-11-29T01:00:00Z') " +
        $"from app_users where id = '{userId}' returning id");

    private async Task<int> Deliver()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDelivery>().RunAsync(CancellationToken.None);
    }

    private static async Task Subscribe(HttpClient client, string endpoint) =>
        (await client.PostAsJsonAsync("/api/v1/me/push-subscriptions", new { endpoint, p256dh = "BKey", auth = "secret" }))
        .StatusCode.Should().Be(HttpStatusCode.NoContent);

    private Task<string> Row(Guid id) => factory.Scalar<string>(
        $"select array_to_string(channels, ',') || '|' || (delivered_at is not null) || '|' || attempts from notifications where id = '{id}'")!;

    [Fact]
    public async Task The_worker_sends_to_the_channels_each_person_chose()
    {
        var endpoint = $"https://push.test/{Guid.NewGuid():N}";
        await Subscribe(Member, endpoint);
        var title = $"Cell {Guid.NewGuid():N}"[..13];
        var id = await Queue(MemberAppId, title);

        await Deliver();
        (await Row(id)).Should().Be("in_app,email,push|true|0");
        var mail = factory.Email.Sent.Single(m => m.Subject == $"Changed: {title}");
        mail.To.Should().Be("member@timecheck.test");
        mail.Text.Should().Contain("Now Sun 29 Nov, 9 AM.").And.Contain("https://app.test/events/");
        factory.Push.Sent.Should().Contain(p => p.Endpoint == endpoint && p.Payload.Contains(title, StringComparison.Ordinal));

        // Events by push only from now on.
        var res = await Member.PutAsJsonAsync("/api/v1/me/notification-preferences",
            new { categories = new[] { new { category = "events", email = false, push = true } } });
        var prefs = await res.Content.ReadFromJsonAsync<JsonElement>();
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        prefs.GetProperty("categories").EnumerateArray().Should().Contain(c => c.GetProperty("category").GetString() == "events" && !c.GetProperty("email").GetBoolean());
        prefs.GetProperty("categories").EnumerateArray().Should().Contain(c => c.GetProperty("category").GetString() == "account" && c.GetProperty("email").GetBoolean(),
            "categories left out keep their choice");
        prefs.GetProperty("pushDevices").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var second = await Queue(MemberAppId, $"{title}b");
        await Deliver();
        (await Row(second)).Should().Be("in_app,push|true|0");
        factory.Email.Sent.Should().NotContain(m => m.Subject == $"Changed: {title}b");

        (await Member.PutAsJsonAsync("/api/v1/me/notification-preferences", new { categories = new[] { new { category = "events", email = true, push = true } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Member.PutAsJsonAsync("/api/v1/me/notification-preferences", new { categories = new[] { new { category = "nope", email = true, push = true } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_failed_email_is_retried_with_backoff_without_repeating_the_push()
    {
        // Member2, briefly with an address the fake provider fails on, and one push device.
        var who = Member2AppId;
        var original = await factory.Scalar<string>($"select email::text from app_users where id = '{who}'");
        var endpoint = $"https://push.test/{Guid.NewGuid():N}";
        await factory.Scalar<int>($"update app_users set email = 'fail-member2@example.test' where id = '{who}' returning 1");
        await factory.Scalar<int>($"insert into push_subscriptions (church_id, user_id, endpoint, p256dh, auth) select church_id, id, '{endpoint}', 'k', 'a' from app_users where id = '{who}' returning 1");
        try
        {
            var id = await Queue(who, "Retry me", "role.changed");
            await Deliver();
            (await Row(id)).Should().EndWith("|false|1");
            (await factory.Scalar<bool>($"select next_attempt_at > now() + interval '90 seconds' and last_error like 'email:%' from notifications where id = '{id}'")).Should().BeTrue();
            factory.Push.Sent.Count(p => p.Endpoint == endpoint).Should().Be(1);

            await factory.Scalar<int>($"update notifications set next_attempt_at = now() where id = '{id}' returning 1");
            await Deliver();
            (await Row(id)).Should().EndWith("|false|2");
            factory.Push.Sent.Count(p => p.Endpoint == endpoint).Should().Be(1, "only the email is retried");
        }
        finally
        {
            await factory.Scalar<int>($"update app_users set email = '{original}' where id = '{who}' returning 1");
            await factory.Scalar<int>($"delete from push_subscriptions where endpoint = '{endpoint}' returning 1");
        }
    }

    [Fact]
    public async Task An_expired_push_subscription_is_removed_and_only_https_endpoints_are_accepted()
    {
        var gone = $"https://push.test/gone-{Guid.NewGuid():N}";
        await Subscribe(Member, gone);
        await Queue(MemberAppId, "Gone test");
        await Deliver();
        (await factory.Scalar<long>($"select count(*) from push_subscriptions where endpoint = '{gone}'")).Should().Be(0);

        (await Member.PostAsJsonAsync("/api/v1/me/push-subscriptions", new { endpoint = "http://169.254.169.254/x", p256dh = "k", auth = "a" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The same browser, then used by the leader, moves to the leader; removing it is per person.
        var shared = $"https://push.test/{Guid.NewGuid():N}";
        await Subscribe(Member, shared);
        await Subscribe(factory.ClientFor(ApiFactory.LeaderAuth), shared);
        (await factory.Scalar<Guid>($"select user_id from push_subscriptions where endpoint = '{shared}'")).Should().Be(ApiFactory.LeaderAppId);
        await Member.PostAsJsonAsync("/api/v1/me/push-subscriptions/remove", new { endpoint = shared });
        (await factory.Scalar<long>($"select count(*) from push_subscriptions where endpoint = '{shared}'")).Should().Be(1);
        await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync("/api/v1/me/push-subscriptions/remove", new { endpoint = shared });
        (await factory.Scalar<long>($"select count(*) from push_subscriptions where endpoint = '{shared}'")).Should().Be(0);
    }

    [Fact]
    public async Task The_in_app_list_pages_newest_first_and_marks_only_your_own_read()
    {
        var older = await Queue(MemberAppId, "Older one");
        var newer = await Queue(MemberAppId, "Newer one");
        var theirs = await Queue(Member2AppId, "Not yours");

        var page = await Member.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications?limit=1");
        var first = page.GetProperty("items")[0];
        first.GetProperty("id").GetGuid().Should().Be(newer);
        first.GetProperty("title").GetString().Should().Be("Changed: Newer one");
        first.GetProperty("url").GetString().Should().StartWith("/events/");
        page.GetProperty("unread").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var next = await Member.GetFromJsonAsync<JsonElement>($"/api/v1/me/notifications?limit=1&cursor={page.GetProperty("nextCursor").GetString()}");
        next.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(older);

        await Member.PostAsJsonAsync("/api/v1/me/notifications/read", new { ids = new[] { newer, theirs } });
        (await factory.Scalar<bool>($"select read_at is not null from notifications where id = '{newer}'")).Should().BeTrue();
        (await factory.Scalar<bool>($"select read_at is null from notifications where id = '{theirs}'")).Should().BeTrue();
        await Member.PostAsJsonAsync("/api/v1/me/notifications/read", new { ids = (Guid[]?)null });
        (await Member.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("unread").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Leaders_hear_once_about_members_still_unmarked_two_hours_after_an_event()
    {
        string Ts(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);
        var ended = DateTimeOffset.UtcNow.AddHours(-3);
        var title = $"Unmarked {Guid.NewGuid():N}"[..17];
        var eventId = await factory.Scalar<Guid>(
            "insert into events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on) " +
            $"select t.church_id, t.id, '{ApiFactory.Group1}', '{title}', '{Ts(ended.AddHours(-1))}', '{Ts(ended)}', current_date " +
            $"from event_types t join cell_groups g on g.church_id = t.church_id and g.id = '{ApiFactory.Group1}' where t.code = 'CELL_MEETING' returning id");
        await factory.Scalar<int>($"insert into attendance (church_id, event_id, user_id, status) select church_id, '{eventId}', '{MemberAppId}', 'PRESENT' from app_users where id = '{MemberAppId}' returning 1");

        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<AttendanceReminders>();
        await job.RunAsync(CancellationToken.None);
        var payload = await factory.Scalar<string>(
            $"select payload::text from notifications where type = 'attendance.unmarked' and user_id = '{ApiFactory.LeaderAppId}' and payload->>'eventId' = '{eventId}'");
        payload.Should().NotBeNull();
        var expected = await factory.Scalar<long>($"select count(*) - 1 from group_memberships where group_id = '{ApiFactory.Group1}' and left_on is null");
        JsonDocument.Parse(payload!).RootElement.GetProperty("members").GetInt64().Should().Be(expected, "everyone but the member marked present");

        await job.RunAsync(CancellationToken.None);
        (await factory.Scalar<long>($"select count(*) from notifications where type = 'attendance.unmarked' and payload->>'eventId' = '{eventId}'")).Should().Be(1);
    }
}
