using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Features.Events;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§5 attendance model, §6.2 attendance, §6.6 scheduling, and the IDOR checks around them.</summary>
[Collection(ApiTestGroup.Name)]
public class EventsTests(ApiFactory factory)
{
    private static readonly Guid MemberAppId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Member2AppId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);

    private HttpClient Leader => factory.ClientFor(ApiFactory.LeaderAuth);
    private HttpClient Admin => factory.ClientFor(ApiFactory.AdminAuth, "aal2");

    private async Task<Guid> TypeId(string code) =>
        (await Leader.GetFromJsonAsync<JsonElement>("/api/v1/event-types")).EnumerateArray()
        .First(t => t.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    /// <summary>Starts in Manila local time; a random minute keeps tests from colliding on the conflict check.</summary>
    private static DateTimeOffset At(int dayOffset, int hour) =>
        new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Add(Manila).Date, DateTimeKind.Unspecified).AddDays(dayOffset).AddHours(hour).AddMinutes(Random.Shared.Next(0, 50)), Manila);

    private static async Task<JsonElement> Create(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/v1/events", body);
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> CellMeeting(DateTimeOffset starts, int weeks = 1, Guid? group = null) => await Create(Leader, new
    {
        eventTypeId = await TypeId("CELL_MEETING"), groupId = group ?? ApiFactory.Group1, title = "Cell Meeting", startsAt = starts,
        endsAt = starts.AddHours(2), location = "The Cruz home", repeatWeeks = weeks, confirmConflicts = true,
    });

    private static object[] Marks(JsonElement detail, Func<JsonElement, string> status) =>
        detail.GetProperty("roster").EnumerateArray().Select(r => (object)new
        {
            userId = r.GetProperty("userId").GetGuid(), status = status(r), note = (string?)null,
            rowVersion = r.GetProperty("rowVersion").ValueKind == JsonValueKind.Null ? (uint?)null : r.GetProperty("rowVersion").GetUInt32(),
        }).ToArray();

    [Fact]
    public async Task The_generator_fills_the_next_14_days_once_and_never_puts_Morning_Prayer_on_Sunday()
    {
        using var scope = factory.Services.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<EventGenerator>();
        await generator.GenerateAsync();
        (await scope.ServiceProvider.GetRequiredService<EventGenerator>().GenerateAsync()).Should().Be(0, "a second run adds nothing");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Add(Manila));
        var last = today.AddDays(13);
        string Range(string code, string extra = "") =>
            $"select count(*) from events e join event_types t on t.id = e.event_type_id where e.is_generated and t.code = '{code}' " +
            $"and e.occurs_on between '{today:yyyy-MM-dd}' and '{last:yyyy-MM-dd}' and e.church_id = (select church_id from cell_groups where id = '{ApiFactory.Group1}') {extra}";
        var weekdays = Enumerable.Range(0, 14).Count(i => today.AddDays(i).DayOfWeek != DayOfWeek.Sunday);
        var sundays = 14 - weekdays;

        (await factory.Scalar<long>(Range("MORNING_PRAYER", $"and e.group_id = '{ApiFactory.Group1}'"))).Should().Be(weekdays);
        (await factory.Scalar<long>(Range("MORNING_PRAYER", "and extract(dow from e.occurs_on) = 0"))).Should().Be(0);
        (await factory.Scalar<long>(Range("CELL_CELEBRATION", "and e.group_id is null"))).Should().Be(sundays);
        (await factory.Scalar<long>(Range("WEEK_OF_FIRE", "and e.group_id is null"))).Should().Be(2, "Fridays, church-wide (§15 Q3)");
        (await factory.Scalar<long>(Range("CELL_MEETING"))).Should().Be(0, "leaders schedule cell meetings");
        (await factory.Scalar<string>(
            $"select to_char(e.starts_at at time zone 'Asia/Manila', 'HH24:MI') from events e join event_types t on t.id = e.event_type_id " +
            $"where t.code = 'MORNING_PRAYER' and e.group_id = '{ApiFactory.Group1}' and e.is_generated order by e.occurs_on limit 1")).Should().Be("05:00");
    }

    [Fact]
    public async Task A_leader_schedules_a_weekly_cell_meeting_and_the_group_is_notified()
    {
        var detail = await CellMeeting(At(20, 19), weeks: 3);
        var series = detail.GetProperty("seriesId").GetGuid();
        detail.GetProperty("canManage").GetBoolean().Should().BeTrue();
        detail.GetProperty("groupName").GetString().Should().Be("Joshua Cell");

        (await factory.Scalar<long>($"select count(*) from events where series_id = '{series}'")).Should().Be(3);
        (await factory.Scalar<string>(
            $"select string_agg(to_char(starts_at at time zone 'Asia/Manila', 'Dy HH24'), ',' order by starts_at) from events where series_id = '{series}'"))
            .Should().MatchRegex(@"^(\w{3} \d\d),\1,\1$", "same weekday and hour each week");
        var id = detail.GetProperty("id").GetGuid();
        (await factory.Scalar<long>($"select count(*) from notifications where type = 'event.scheduled' and payload->>'eventId' = '{id}' and user_id = '{MemberAppId}'"))
            .Should().Be(1);
        (await factory.Scalar<string>($"select after::text from audit_logs where action = 'event.create' and entity_id = '{id}'")).Should().Contain("CELL_MEETING");
    }

    [Fact]
    public async Task Scheduling_is_limited_by_role_and_group()
    {
        var starts = At(25, 18);
        object Body(string type, Guid? group) => new { eventTypeId = type, groupId = group, startsAt = starts, endsAt = starts.AddHours(2), confirmConflicts = true };
        var cell = (await TypeId("CELL_MEETING")).ToString();
        var pepsol = (await TypeId("PEPSOL")).ToString();
        var prayer = (await TypeId("MORNING_PRAYER")).ToString();

        (await Leader.PostAsJsonAsync("/api/v1/events", Body(cell, ApiFactory.Group2))).StatusCode.Should().Be(HttpStatusCode.NotFound, "not their group");
        (await Leader.PostAsJsonAsync("/api/v1/events", Body(pepsol, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "church-wide is for admins");
        (await Leader.PostAsJsonAsync("/api/v1/events", Body(prayer, ApiFactory.Group1))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "generated");
        (await Leader.PostAsJsonAsync("/api/v1/events", Body(cell, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "needs a group");
        (await factory.ClientFor(ApiFactory.MemberAuth).PostAsJsonAsync("/api/v1/events", Body(cell, ApiFactory.Group1))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await Admin.PostAsJsonAsync("/api/v1/events", Body(pepsol, null))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Admin.PostAsJsonAsync("/api/v1/events", Body(cell, ApiFactory.Group2))).StatusCode.Should().Be(HttpStatusCode.Created, "admins schedule for any group");
    }

    [Fact]
    public async Task A_clash_warns_until_confirmed()
    {
        var starts = At(30, 20);
        var first = await CellMeeting(starts);
        var body = new { eventTypeId = await TypeId("PID_ILD"), groupId = ApiFactory.Group1, startsAt = starts.AddMinutes(30), endsAt = starts.AddHours(1) };

        var res = await Leader.PostAsJsonAsync("/api/v1/events", body);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("event.conflict");
        problem.GetProperty("conflicts")[0].GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());

        (await Leader.PostAsJsonAsync("/api/v1/events", new { body.eventTypeId, body.groupId, body.startsAt, body.endsAt, confirmConflicts = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Mark_all_present_then_an_exception_with_concurrency_and_audit()
    {
        var evt = await CellMeeting(At(0, 0).AddMinutes(1));
        var id = evt.GetProperty("id").GetGuid();
        var roster = evt.GetProperty("roster").EnumerateArray().Select(r => r.GetProperty("userId").GetGuid()).ToList();
        roster.Should().Contain([ApiFactory.LeaderAppId, MemberAppId], "the group (other tests add people to it too)");

        // 1: mark everyone present.
        var res = await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = Marks(evt, _ => "PRESENT") });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var marked = await res.Content.ReadFromJsonAsync<JsonElement>();
        marked.GetProperty("roster").EnumerateArray().Should().OnlyContain(r => r.GetProperty("status").GetString() == "PRESENT");

        // A phone retrying the same request changes nothing and doesn't trip the concurrency check.
        (await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = Marks(evt, _ => "PRESENT") })).StatusCode.Should().Be(HttpStatusCode.OK);

        // 2: the exception. An admin changes the member first, so the leader's row version is stale.
        var member = Marks(marked, r => r.GetProperty("userId").GetGuid() == MemberAppId ? "LATE" : "PRESENT");
        (await Admin.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = member })).StatusCode.Should().Be(HttpStatusCode.OK);
        var stale = await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = Marks(marked, r => r.GetProperty("userId").GetGuid() == MemberAppId ? "ABSENT" : "PRESENT") });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("stale");

        var fresh = await Leader.GetFromJsonAsync<JsonElement>($"/api/v1/events/{id}");
        (await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = Marks(fresh, r => r.GetProperty("userId").GetGuid() == MemberAppId ? "ABSENT" : "PRESENT") }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.Scalar<string>($"select status::text from attendance where event_id = '{id}' and user_id = '{MemberAppId}'")).Should().Be("ABSENT");

        var audit = await factory.Scalar<string>($"select before::text || after::text from audit_logs where action = 'attendance.mark' and entity_id = '{id}' order by occurred_at desc limit 1");
        audit.Should().Contain("LATE").And.Contain("ABSENT");

        // The member's own history and the leader's Today agree.
        var history = await factory.ClientFor(ApiFactory.MemberAuth).GetFromJsonAsync<JsonElement>("/api/v1/me/attendance");
        history.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("eventId").GetGuid() == id && i.GetProperty("status").GetString() == "ABSENT");
        var today = await Leader.GetFromJsonAsync<JsonElement>("/api/v1/me/today");
        var pass = today.GetProperty("today").EnumerateArray().First(e => e.GetProperty("id").GetGuid() == id);
        pass.GetProperty("marking").GetProperty("marked").GetInt32().Should().Be(roster.Count);
        pass.GetProperty("marking").GetProperty("present").GetInt32().Should().Be(roster.Count - 1);
    }

    [Fact]
    public async Task People_outside_your_reach_are_refused()
    {
        var evt = await CellMeeting(At(0, 0).AddMinutes(2));
        var id = evt.GetProperty("id").GetGuid();
        var mark = new { marks = new[] { new { userId = Member2AppId, status = "PRESENT" } } };

        (await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", mark)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "member2 is in another group");
        (await factory.ClientFor(ApiFactory.Leader2Auth).GetAsync($"/api/v1/events/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(ApiFactory.Leader2Auth).PatchAsJsonAsync($"/api/v1/events/{id}", new { startsAt = At(0, 9), endsAt = At(0, 10) }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(ApiFactory.MemberAuth).PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = new[] { new { userId = MemberAppId, status = "PRESENT" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "members don't mark (§15 Q2)");
        (await factory.ClientFor(ApiFactory.MemberAuth).GetFromJsonAsync<JsonElement>($"/api/v1/events/{id}"))
            .GetProperty("roster").ValueKind.Should().Be(JsonValueKind.Null, "members see the event, not the roster");
        (await factory.ClientFor(ApiFactory.OtherChurchAuth).GetAsync($"/api/v1/events/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Marking_waits_for_the_day_and_closes_after_the_edit_window_except_for_admins()
    {
        var future = await CellMeeting(At(3, 19));
        (await Leader.PutAsJsonAsync($"/api/v1/events/{future.GetProperty("id").GetGuid()}/attendance", new { marks = Marks(future, _ => "PRESENT") }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var old = await CellMeeting(At(-10, 19));
        var oldId = old.GetProperty("id").GetGuid();
        old.GetProperty("canEditMarks").GetBoolean().Should().BeFalse();
        (await Leader.PutAsJsonAsync($"/api/v1/events/{oldId}/attendance", new { marks = Marks(old, _ => "PRESENT") })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Admin.PutAsJsonAsync($"/api/v1/events/{oldId}/attendance", new { marks = Marks(old, _ => "EXCUSED") })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Church_wide_events_split_the_roster_by_group()
    {
        var starts = At(0, 0).AddMinutes(3);
        var evt = await Create(Admin, new { eventTypeId = await TypeId("CONFERENCE"), title = "Youth Conference", startsAt = starts, endsAt = starts.AddHours(20), confirmConflicts = true });
        var id = evt.GetProperty("id").GetGuid();
        evt.GetProperty("isChurchWide").GetBoolean().Should().BeTrue();
        evt.GetProperty("roster").GetArrayLength().Should().BeGreaterThanOrEqualTo(6, "admins see everyone");

        var leaderView = await Leader.GetFromJsonAsync<JsonElement>($"/api/v1/events/{id}");
        leaderView.GetProperty("canManage").GetBoolean().Should().BeFalse();
        leaderView.GetProperty("roster").EnumerateArray().Should().OnlyContain(r => r.GetProperty("groupId").GetGuid() == ApiFactory.Group1);
        (await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = Marks(leaderView, _ => "PRESENT") })).StatusCode.Should().Be(HttpStatusCode.OK);

        var adminGroup2 = await Admin.GetFromJsonAsync<JsonElement>($"/api/v1/events/{id}?groupId={ApiFactory.Group2}");
        adminGroup2.GetProperty("roster").EnumerateArray().Should().OnlyContain(r => r.GetProperty("groupId").GetGuid() == ApiFactory.Group2);

        var memberToday = await factory.ClientFor(ApiFactory.MemberAuth).GetFromJsonAsync<JsonElement>("/api/v1/me/today");
        memberToday.GetProperty("today").EnumerateArray().Should().Contain(e => e.GetProperty("id").GetGuid() == id && e.GetProperty("myStatus").GetString() == "PRESENT");
        (await Leader.PostAsync($"/api/v1/events/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "admin-scheduled");
    }

    [Fact]
    public async Task Cancelling_notifies_the_group_and_drops_it_from_lists_and_marking()
    {
        var evt = await CellMeeting(At(0, 0).AddMinutes(4));
        var id = evt.GetProperty("id").GetGuid();
        var res = await Leader.PostAsync($"/api/v1/events/{id}/cancel", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isCancelled").GetBoolean().Should().BeTrue();

        (await factory.Scalar<long>($"select count(*) from notifications where type = 'event.cancelled' and payload->>'eventId' = '{id}' and user_id = '{MemberAppId}'")).Should().Be(1);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Add(Manila)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        (await Leader.GetFromJsonAsync<JsonElement>($"/api/v1/events?from={day}&to={day}")).EnumerateArray()
            .Should().NotContain(e => e.GetProperty("id").GetGuid() == id);
        (await Leader.PutAsJsonAsync($"/api/v1/events/{id}/attendance", new { marks = new[] { new { userId = MemberAppId, status = "PRESENT" } } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_leader_moves_an_event_and_the_audit_keeps_the_old_time()
    {
        var evt = await CellMeeting(At(40, 19));
        var id = evt.GetProperty("id").GetGuid();
        var moved = At(41, 18);
        var res = await Leader.PatchAsJsonAsync($"/api/v1/events/{id}", new { title = "Cell Meeting (moved)", startsAt = moved, endsAt = moved.AddHours(2), location = "Church", confirmConflicts = true });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("occursOn").GetString().Should().Be(DateOnly.FromDateTime(moved.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        (await factory.Scalar<long>($"select count(*) from notifications where type = 'event.changed' and payload->>'eventId' = '{id}' and user_id = '{MemberAppId}'")).Should().Be(1);
        (await factory.Scalar<string>($"select before::text from audit_logs where action = 'event.update' and entity_id = '{id}'")).Should().Contain("Cell Meeting\"");
    }

    [Fact]
    public async Task The_morning_prayer_streak_counts_back_until_a_miss()
    {
        // Three past Morning Prayers for a fresh group: present, present, absent (oldest). Streak = 2.
        var group = await factory.Scalar<Guid>(
            "insert into cell_groups (church_id, name) select id, 'Streak ' || gen_random_uuid() from churches order by created_at limit 1 returning id");
        // member2 moves into the fresh group for this test and back afterwards.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Add(Manila));
        var days = Enumerable.Range(1, 10).Select(i => today.AddDays(-i)).Where(d => d.DayOfWeek != DayOfWeek.Sunday).Take(3).ToList();
        await factory.Scalar<int>($"update group_memberships set left_on = current_date where user_id = '{Member2AppId}' and left_on is null returning 1");
        await factory.Scalar<int>($"insert into group_memberships (church_id, group_id, user_id) select church_id, '{group}', id from app_users where id = '{Member2AppId}' returning 1");
        try
        {
            var statuses = new[] { "PRESENT", "PRESENT", "ABSENT" };
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await factory.Scalar<int>(
                    $"with e as (insert into events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on, is_generated) " +
                    $"select t.church_id, t.id, '{group}', 'Morning Prayer', '{d} 05:00+08', '{d} 06:00+08', '{d}', true from event_types t " +
                    $"join churches c on c.id = t.church_id where t.code = 'MORNING_PRAYER' order by c.created_at limit 1 returning id, church_id) " +
                    $"insert into attendance (church_id, event_id, user_id, status) select church_id, id, '{Member2AppId}', '{statuses[i]}' from e returning 1");
            }
            await ForgetMember2();
            var history = await factory.ClientFor(MemberTwoAuth).GetFromJsonAsync<JsonElement>("/api/v1/me/attendance");
            history.GetProperty("morningPrayerStreak").GetInt32().Should().Be(2);
            history.GetProperty("attended").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            await factory.Scalar<int>($"delete from group_memberships where user_id = '{Member2AppId}' and group_id = '{group}' returning 1");
            await factory.Scalar<int>($"update group_memberships set left_on = null where user_id = '{Member2AppId}' and group_id = '{ApiFactory.Group2}' returning 1");
            await ForgetMember2();
        }
    }

    /// <summary>The cached user carries their group, so drop it whenever the test moves member2.</summary>
    private async Task ForgetMember2()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TimeCheck.Application.Abstractions.IUserDirectory>().InvalidateAsync(MemberTwoAuth, CancellationToken.None);
    }

    private static readonly Guid MemberTwoAuth = Guid.Parse("30000000-0000-0000-0000-000000000006");
}
