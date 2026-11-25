using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Features.Reports;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>
/// §6.8: report numbers equal the raw attendance rows, each role sees only its reports and groups, results are cached until
/// the next write, and exports are private files that expire.
/// </summary>
[Collection(ApiTestGroup.Name)]
public class ReportsTests(ApiFactory factory)
{
    private const string Joshua = "20000000-0000-0000-0000-000000000001";
    private const string Caleb = "20000000-0000-0000-0000-000000000002";
    private const string Leader = "10000000-0000-0000-0000-000000000003";
    private const string Member = "10000000-0000-0000-0000-000000000004";
    private const string Member2 = "10000000-0000-0000-0000-000000000006";

    private HttpClient Admin => factory.ClientFor(ApiFactory.AdminAuth, "aal2");
    private HttpClient LeaderClient => factory.ClientFor(ApiFactory.LeaderAuth);

    /// <summary>
    /// A fresh event type with two Joshua Cell meetings and one church-wide event in March, marked by hand, so this test's numbers
    /// are its own. Returns the type id.
    /// </summary>
    private async Task<Guid> SeedMarch()
    {
        var code = $"RPT_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var type = await factory.Scalar<Guid>(
            "insert into event_types (church_id, code, name, category, cadence, default_scope) " +
            $"select church_id, '{code}', 'Report test {code[4..10]}', 'LEADER_SCHEDULED', 'AD_HOC', 'GROUP' from cell_groups where id = '{Joshua}' returning id");
        async Task<Guid> Event(string? group, string day) => await factory.Scalar<Guid>(
            "insert into events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on) " +
            $"select church_id, '{type}', {(group is null ? "null" : $"'{group}'")}, 'Report test', '{day}T11:00:00Z', '{day}T13:00:00Z', '{day}' " +
            $"from event_types where id = '{type}' returning id");
        var e1 = await Event(Joshua, "2026-03-02");
        var e2 = await Event(Joshua, "2026-03-09");
        var all = await Event(null, "2026-03-04");
        async Task Mark(Guid e, string user, string status) => await factory.Scalar<int>(
            $"insert into attendance (church_id, event_id, user_id, status) select church_id, id, '{user}', '{status}' from events where id = '{e}' returning 1");
        await Mark(e1, Member, "PRESENT");
        await Mark(e1, Leader, "EXCUSED");
        await Mark(e2, Member, "LATE");
        await Mark(all, Member, "PRESENT");
        await Mark(all, Member2, "ABSENT");
        return type;
    }

    /// <summary>The same totals straight from the tables, without the view: expected people per event, and their marks.</summary>
    private Task<string> Raw(Guid type, string? group = null) => factory.Scalar<string>($"""
        with ev as (select id, church_id, group_id, occurs_on from events where event_type_id = '{type}' and cancelled_at is null),
        exp as (
          select ev.id, m.user_id from ev join group_memberships m on m.group_id = ev.group_id
            and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on)
          union
          select ev.id, u.id from ev join app_users u on u.church_id = ev.church_id and ev.group_id is null and u.deleted_at is null
            and u.joined_on <= ev.occurs_on and (u.deactivated_at is null or u.deactivated_at::date > ev.occurs_on)
          union
          select event_id, user_id from attendance where event_id in (select id from ev)),
        rows as (
          select a.status from exp join ev on ev.id = exp.id left join attendance a on a.event_id = exp.id and a.user_id = exp.user_id
          {(group is null ? "" : $"where exists (select 1 from group_memberships m where m.user_id = exp.user_id and m.group_id = '{group}' and m.joined_on <= ev.occurs_on and (m.left_on is null or m.left_on > ev.occurs_on))")})
        select count(*) || '|' || count(*) filter (where status in ('PRESENT', 'LATE')) || '|' || count(*) filter (where status = 'EXCUSED')
          || '|' || count(*) filter (where status = 'ABSENT') || '|' || count(*) filter (where status is null) from rows
        """)!;

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Section(JsonElement report, string key) =>
        report.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);

    private static List<JsonElement[]> Rows(JsonElement section) =>
        section.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().ToArray()).ToList();

    /// <summary>expected|attended|excused|absent|unmarked from the "By event type" row.</summary>
    private static string Totals(JsonElement report)
    {
        var row = Rows(Section(report, "byType")).Single();
        int N(int i) => row[i].GetInt32();
        return $"{N(2)}|{N(3) + N(4)}|{N(5)}|{N(6)}|{N(7)}";
    }

    [Fact]
    public async Task Attendance_numbers_equal_the_raw_rows_for_admins_and_leaders()
    {
        var type = await SeedMarch();
        var q = $"from=2026-03-01&to=2026-03-31&eventTypeId={type}";

        // Who is expected depends on who else the other tests registered with an early join date, so compare with the raw rows
        // and pin only the marks this test made.
        var church = await Json(await Admin.GetAsync($"/api/v1/reports/attendance?{q}"));
        var raw = await Raw(type);
        Totals(church).Should().Be(raw);
        raw.Split('|')[1..4].Should().Equal("3", "1", "1");
        var total = int.Parse(raw.Split('|')[0], System.Globalization.CultureInfo.InvariantCulture);
        total.Should().BeGreaterThanOrEqualTo(10, "2 + 2 at the meetings and the 6 seeded people at the church-wide event");
        Rows(Section(church, "byType")).Single()[8].GetDecimal().Should().Be(Math.Round(300m / (total - 1), 1, MidpointRounding.AwayFromZero));
        var member = Rows(Section(church, "byMember")).Single(r => r[0].GetString() == "Nico Bautista");
        (member[2].GetInt32(), member[3].GetInt32(), member[4].GetDecimal()).Should().Be((3, 3, 100m));
        church.GetProperty("scope").EnumerateArray().Select(s => s.GetString()).Should().Contain(["1 Mar 2026 – 31 Mar 2026", "All groups"]);

        // The leader's own group only, including the church-wide event for its members.
        var mine = await Json(await LeaderClient.GetAsync($"/api/v1/reports/attendance?{q}"));
        var rawMine = await Raw(type, Joshua);
        Totals(mine).Should().Be(rawMine);
        rawMine.Split('|')[1..4].Should().Equal("3", "1", "0");
        Rows(Section(mine, "byMember")).Select(r => r[0].GetString()).Should().Contain(["Joy Dela Cruz", "Nico Bautista"]).And.NotContain("Lia Ramos");
    }

    [Fact]
    public async Task Cached_results_last_until_the_churchs_next_write()
    {
        var type = await SeedMarch();
        var url = $"/api/v1/reports/attendance?from=2026-03-01&to=2026-03-31&eventTypeId={type}";
        var before = Totals(await Json(await Admin.GetAsync(url)));
        before.Should().Be(await Raw(type));

        // A change made behind the API's back isn't seen: the result is cached...
        await factory.Scalar<int>($"update attendance set status = 'PRESENT' where user_id = '{Member2}' and event_id in (select id from events where event_type_id = '{type}') returning 1");
        Totals(await Json(await Admin.GetAsync(url))).Should().Be(before);

        // ...until any audited write in the church.
        (await LeaderClient.PutAsJsonAsync("/api/v1/me/notification-preferences", new { categories = new[] { new { category = "events", email = true, push = true } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var after = Totals(await Json(await Admin.GetAsync(url)));
        after.Should().Be(await Raw(type)).And.NotBe(before);
        after.Split('|')[1..4].Should().Equal("4", "1", "0");
    }

    [Fact]
    public async Task Each_role_sees_only_its_reports_and_groups()
    {
        (await factory.ClientFor(ApiFactory.MemberAuth).GetAsync("/api/v1/reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var leaderList = await Json(await LeaderClient.GetAsync("/api/v1/reports"));
        leaderList.EnumerateArray().Select(r => r.GetProperty("type").GetString()).Should().BeEquivalentTo(["attendance", "member-status", "members"]);
        (await Json(await Admin.GetAsync("/api/v1/reports"))).GetArrayLength().Should().Be(6);

        (await LeaderClient.GetAsync("/api/v1/reports/groups")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeaderClient.GetAsync($"/api/v1/reports/attendance?groupId={Caleb}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Admin.GetAsync("/api/v1/reports/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Admin.GetAsync("/api/v1/reports/attendance?from=2025-01-01&to=2026-06-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await factory.ClientFor(ApiFactory.OtherChurchAuth).GetAsync($"/api/v1/reports/attendance?groupId={Joshua}")).StatusCode
            .Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        // Member lists stay within the leader's group.
        var members = await Json(await LeaderClient.GetAsync("/api/v1/reports/members"));
        Rows(Section(members, "members")).Select(r => r[1].GetString()).Should().OnlyContain(g => g == "Joshua Cell");
        Rows(Section(members, "counts")).Should().Contain(r => r[0].GetString() == "Status");
        var status = await Json(await LeaderClient.GetAsync("/api/v1/reports/member-status"));
        Rows(Section(status, "members")).Select(r => r[0].GetString()).Should().Contain(["Joy Dela Cruz", "Nico Bautista"]).And.NotContain("Lia Ramos");
    }

    [Fact]
    public async Task Admin_reports_cover_every_group()
    {
        await SeedMarch();
        var q = "from=2026-03-01&to=2026-03-31";
        var groups = await Json(await Admin.GetAsync($"/api/v1/reports/groups?{q}"));
        Rows(Section(groups, "groups")).Select(r => r[0].GetString()).Should().Contain(["Joshua Cell", "Caleb Cell"]);
        var cells = await Json(await Admin.GetAsync($"/api/v1/reports/cells?{q}"));
        Rows(Section(cells, "cells")).Single(r => r[0].GetString() == "Joshua Cell")[2].GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var tree = await Json(await Admin.GetAsync("/api/v1/reports/hierarchy"));
        Rows(Section(tree, "hierarchy")).Should().Contain(r => r[0].GetString() == "Joshua Cell" && r[2].GetString() == "Nico Bautista" && r[1].GetString() == "Joy Dela Cruz");
        tree.GetProperty("chart").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Exports_are_private_files_that_expire()
    {
        var type = await SeedMarch();
        var filters = new { from = "2026-03-01", to = "2026-03-31", eventTypeId = type };

        var res = await Admin.PostAsJsonAsync("/api/v1/reports/attendance/exports", new { format = "csv", filters });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var job = await res.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("status").GetString().Should().Be("DONE", "a small report is ready in the response");
        var id = job.GetProperty("id").GetGuid();
        var csv = Encoding.UTF8.GetString(factory.Storage.Objects.Single(o => o.Key.EndsWith($"{id}.csv", StringComparison.Ordinal)).Value.Bytes);
        csv.Should().Contain("Event type,Events,Expected,Present,Late,Excused,Absent,Unmarked,Rate").And.Contain(",2,1,1,1,", "two present, one late, one excused, one absent");
        (await factory.Scalar<string>($"select action from audit_logs where entity_id = '{id}'")).Should().Be("report.export");

        var link = await Json(await Admin.GetAsync($"/api/v1/me/report-jobs/{id}/download"));
        link.GetProperty("url").GetString().Should().StartWith("https://storage.test/sign/exports/").And.Contain("download=timecheck-attendance-");
        (await LeaderClient.GetAsync($"/api/v1/me/report-jobs/{id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound, "only the person who asked");
        (await Json(await Admin.GetAsync("/api/v1/me/report-jobs"))).EnumerateArray().Should().Contain(j => j.GetProperty("id").GetGuid() == id);

        foreach (var format in new[] { "xlsx", "pdf" })
            (await Admin.PostAsJsonAsync("/api/v1/reports/attendance/exports", new { format, filters })).StatusCode.Should().Be(HttpStatusCode.Created);
        var xlsx = factory.Storage.Objects.Last(o => o.Key.EndsWith(".xlsx", StringComparison.Ordinal)).Value.Bytes;
        using (var zip = new ZipArchive(new MemoryStream(xlsx))) zip.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
        Encoding.ASCII.GetString(factory.Storage.Objects.Last(o => o.Key.EndsWith(".pdf", StringComparison.Ordinal)).Value.Bytes).Should().StartWith("%PDF");
        (await Admin.PostAsJsonAsync("/api/v1/reports/attendance/exports", new { format = "docx", filters })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A queued export (too big for the request) is made by the Worker as the requester, who is notified.
        var queued = await factory.Scalar<Guid>(
            "insert into report_jobs (church_id, requested_by, type, params) select church_id, id, 'members', " +
            """'{"format":"pdf","filters":{},"scope":["As of today"]}'""" + $" from app_users where id = '{Leader}' returning id");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReportExports>().RunAsync(CancellationToken.None);
        (await factory.Scalar<string>($"select status::text || coalesce(': ' || error, '') from report_jobs where id = '{queued}'")).Should().Be("DONE");
        (await factory.Scalar<long>($"select count(*) from notifications where type = 'report.ready' and payload->>'jobId' = '{queued}' and user_id = '{Leader}'")).Should().Be(1);
        factory.Storage.Objects.Keys.Should().Contain(k => k.EndsWith($"{queued}.pdf", StringComparison.Ordinal));

        // After 24 hours the file is deleted and the link stops working.
        await factory.Scalar<int>($"update report_jobs set expires_at = now() - interval '1 minute' where id in ('{queued}', '{id}') returning 1");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReportExports>().RunAsync(CancellationToken.None);
        (await factory.Scalar<string>($"select string_agg(status::text, ',') from report_jobs where id in ('{queued}', '{id}')")).Should().Be("EXPIRED,EXPIRED");
        factory.Storage.Objects.Keys.Should().NotContain(k => k.Contains(queued.ToString(), StringComparison.Ordinal) || k.Contains(id.ToString(), StringComparison.Ordinal));
        (await Admin.GetAsync($"/api/v1/me/report-jobs/{id}/download")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
