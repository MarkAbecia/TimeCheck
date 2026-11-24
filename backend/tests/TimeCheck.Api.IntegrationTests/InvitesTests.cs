using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Features.Invites;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.5 guest invites: logging, marking, converting, privacy of guest details, and retention.</summary>
[Collection(ApiTestGroup.Name)]
public class InvitesTests(ApiFactory factory)
{
    private static readonly Guid MemberAppId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Member2Auth = Guid.Parse("30000000-0000-0000-0000-000000000006");

    private HttpClient Member => factory.ClientFor(ApiFactory.MemberAuth);
    private HttpClient Leader => factory.ClientFor(ApiFactory.LeaderAuth);

    /// <summary>A Group 1 event of the given type, starting <paramref name="hoursFromNow"/> hours from now and lasting 2 hours.</summary>
    private async Task<Guid> Event(double hoursFromNow, string type = "CELL_MEETING")
    {
        var starts = DateTimeOffset.UtcNow.AddHours(hoursFromNow);
        var occurs = starts.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Ts(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);
        return await factory.Scalar<Guid>(
            "insert into events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on) " +
            $"select t.church_id, t.id, '{ApiFactory.Group1}', 'Test {type}', '{Ts(starts)}', '{Ts(starts.AddHours(2))}', '{occurs}' " +
            $"from event_types t join cell_groups g on g.church_id = t.church_id and g.id = '{ApiFactory.Group1}' where t.code = '{type}' returning id");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        res.StatusCode.Should().Be(expected, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> Log(Guid eventId, string name = "Ana Reyes", string? contact = "0917 555 0101") =>
        await Json(await Member.PostAsJsonAsync("/api/v1/invites", new { eventId, guestName = name, guestContact = contact }), HttpStatusCode.Created);

    private static Task<HttpResponseMessage> Mark(HttpClient c, Guid id, string status) =>
        c.PutAsJsonAsync($"/api/v1/invites/{id}/status", new { status });

    private static async Task<string?> Code(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task A_member_logs_a_guest_whose_contact_is_encrypted_and_kept_out_of_the_audit()
    {
        var invite = await Log(await Event(48), $"Ana Reyes {Guid.NewGuid():N}"[..20]);
        var id = invite.GetProperty("id").GetGuid();
        invite.GetProperty("status").GetString().Should().Be("EXPECTED");
        invite.GetProperty("guestContact").GetString().Should().Be("0917 555 0101");
        invite.GetProperty("mine").GetBoolean().Should().BeTrue();

        (await factory.Scalar<string>($"select guest_contact_enc from invites where id = '{id}'")).Should().NotContain("0917");
        var audit = await factory.Scalar<string>($"select after::text from audit_logs where action = 'invite.log' and entity_id = '{id}'");
        audit.Should().Contain("hasContact").And.NotContain("Ana").And.NotContain("0917");

        var mine = await Member.GetFromJsonAsync<JsonElement>("/api/v1/me/invites");
        mine.GetProperty("invites").EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == id);
        mine.GetProperty("events").EnumerateArray().Should().OnlyContain(e =>
            e.GetProperty("typeCode").GetString() == "CELL_MEETING" || e.GetProperty("typeCode").GetString() == "CELL_CELEBRATION");

        var edited = await Json(await Member.PatchAsJsonAsync($"/api/v1/invites/{id}", new { guestName = "Ana R.", guestContact = "" }));
        edited.GetProperty("guestContact").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Only_the_inviter_their_leader_and_admins_reach_an_invite()
    {
        var id = (await Log(await Event(48))).GetProperty("id").GetGuid();

        (await Leader.GetFromJsonAsync<JsonElement>("/api/v1/invites")).EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == id);
        (await factory.ClientFor(ApiFactory.AdminAuth, "aal2").GetFromJsonAsync<JsonElement>("/api/v1/invites")).EnumerateArray()
            .Should().Contain(i => i.GetProperty("id").GetGuid() == id);
        (await factory.ClientFor(ApiFactory.Leader2Auth).GetFromJsonAsync<JsonElement>("/api/v1/invites")).EnumerateArray()
            .Should().NotContain(i => i.GetProperty("id").GetGuid() == id);

        foreach (var outsider in new[] { factory.ClientFor(Member2Auth), factory.ClientFor(ApiFactory.Leader2Auth), factory.ClientFor(ApiFactory.OtherChurchAuth) })
        {
            (await outsider.PatchAsJsonAsync($"/api/v1/invites/{id}", new { guestName = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Mark(outsider, id, "CAME")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await outsider.DeleteAsync($"/api/v1/invites/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await Member.GetAsync("/api/v1/invites")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the group list is for leaders");
    }

    [Fact]
    public async Task Guests_are_invited_only_to_cell_meetings_and_celebrations_that_are_on()
    {
        var prayer = await Member.PostAsJsonAsync("/api/v1/invites", new { eventId = await Event(48, "MORNING_PRAYER"), guestName = "Ana" });
        prayer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Code(prayer)).Should().Be("invite.event_type");

        var cancelled = await Event(48);
        await factory.Scalar<int>($"update events set cancelled_at = now() where id = '{cancelled}' returning 1");
        (await Member.PostAsJsonAsync("/api/v1/invites", new { eventId = cancelled, guestName = "Ana" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        (await Member.PostAsJsonAsync("/api/v1/invites", new { eventId = await Event(48), guestName = " " })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await Member.PostAsJsonAsync("/api/v1/invites", new { eventId = Guid.NewGuid(), guestName = "Ana" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_guest_who_came_is_registered_by_the_leader_and_the_invite_is_then_fixed()
    {
        var future = (await Log(await Event(48))).GetProperty("id").GetGuid();
        var early = await Mark(Member, future, "CAME");
        early.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Code(early)).Should().Be("invite.too_early");

        var id = (await Log(await Event(-3))).GetProperty("id").GetGuid();
        var came = await Json(await Mark(Member, id, "CAME"));
        came.GetProperty("canConvert").GetBoolean().Should().BeFalse("the member can't register people");
        (await Json(await Mark(Leader, id, "DID_NOT_COME"))).GetProperty("status").GetString().Should().Be("DID_NOT_COME");
        (await Json(await Mark(Leader, id, "CAME"))).GetProperty("canConvert").GetBoolean().Should().BeTrue();

        var res = await Leader.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", new
        {
            firstName = "Ana", lastName = $"Reyes{Guid.NewGuid():N}"[..14], gender = "FEMALE", birthdate = "2001-05-02",
            mobile = $"0917{Random.Shared.Next(1000000, 9999999)}", email = $"g{Guid.NewGuid():N}@example.test", inviteId = id,
        });
        var member = await Json(res, HttpStatusCode.Created);
        var converted = await factory.Scalar<Guid>($"select converted_user_id from invites where id = '{id}'");
        converted.Should().Be(member.GetProperty("id").GetGuid());

        var locked = await Mark(Leader, id, "DID_NOT_COME");
        locked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Code(locked)).Should().Be("invite.converted");
        (await Member.DeleteAsync($"/api/v1/invites/{id}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Removing_an_invite_erases_the_guest_and_cancelling_the_event_tells_the_inviter()
    {
        var gone = (await Log(await Event(48))).GetProperty("id").GetGuid();
        (await Member.DeleteAsync($"/api/v1/invites/{gone}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.Scalar<string>($"select guest_name || coalesce(guest_contact_enc, '-') || (deleted_at is not null) from invites where id = '{gone}'"))
            .Should().Be($"{TimeCheck.Domain.Entities.Invite.ErasedName}-true");
        (await Member.PatchAsJsonAsync($"/api/v1/invites/{gone}", new { guestName = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var eventId = await Event(72);
        await Log(eventId);
        await Log(eventId, "Ben Cruz", null);
        (await Leader.PostAsync($"/api/v1/events/{eventId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await factory.Scalar<string>(
            $"select payload::text from notifications where user_id = '{MemberAppId}' and type = 'invite.event_cancelled' and payload->>'eventId' = '{eventId}'");
        payload.Should().Contain("\"guests\": 2");
    }

    [Fact]
    public async Task The_worker_erases_unconverted_guests_after_their_date_and_reminds_inviters_once_a_day()
    {
        var past = await Event(-5); // ended 3 hours ago
        var stale = (await Log(past)).GetProperty("id").GetGuid();
        await factory.Scalar<int>($"update invites set purge_after = current_date - 1 where id = '{stale}' returning 1");
        var kept = (await Log(past, "Kept Guest")).GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var reminders = scope.ServiceProvider.GetRequiredService<InviteReminders>();
            await factory.Scalar<int>($"delete from notifications where user_id = '{MemberAppId}' and type = '{InviteReminders.Type}' returning 1");
            (await reminders.RunAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);
            (await reminders.RunAsync(CancellationToken.None)).Should().Be(0, "one reminder per person per day");
            (await scope.ServiceProvider.GetRequiredService<InvitePurge>().RunAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);
        }

        (await factory.Scalar<string>($"select guest_name from invites where id = '{stale}'")).Should().Be(TimeCheck.Domain.Entities.Invite.ErasedName);
        (await factory.Scalar<bool>($"select guest_contact_enc is null and deleted_at is not null from invites where id = '{stale}'")).Should().BeTrue();
        (await factory.Scalar<string>($"select guest_name from invites where id = '{kept}'")).Should().Be("Kept Guest");
    }
}
