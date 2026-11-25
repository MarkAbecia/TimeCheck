using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.10 settings, first pass: age brackets and retention, super admins only, audited, other keys untouched.</summary>
[Collection(ApiTestGroup.Name)]
public class SettingsTests(ApiFactory factory)
{
    private HttpClient SuperAdmin => factory.ClientFor(ApiFactory.SuperAdminAuth, "aal2");

    [Fact]
    public async Task Super_admin_changes_age_brackets_and_retention_and_the_rest_is_kept()
    {
        var original = await SuperAdmin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        original.GetProperty("ageBrackets").EnumerateArray().Select(a => a.GetInt32()).Should().Equal(12, 17, 24, 35);
        original.GetProperty("retention").GetProperty("guestInviteDays").GetInt32().Should().Be(90);
        try
        {
            var res = await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", new
            {
                ageBrackets = (int[])[14, 21, 30],
                retention = new { guestInviteDays = 60, deactivatedUserYears = 2, auditLogDays = 730 },
            });
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

            var now = await SuperAdmin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
            now.GetProperty("ageBrackets").EnumerateArray().Select(a => a.GetInt32()).Should().Equal(14, 21, 30);
            now.GetProperty("retention").GetProperty("auditLogDays").GetInt32().Should().Be(730);
            (await factory.Scalar<bool>("select (settings->'ai'->>'enabled')::boolean and settings->'devotion' is not null from public.churches where id = (select church_id from public.app_users where auth_user_id = '30000000-0000-0000-0000-000000000001')"))
                .Should().BeTrue("keys this screen doesn't own are kept");

            var log = await SuperAdmin.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-logs?action=settings.update&limit=1");
            var entry = log.GetProperty("items")[0];
            entry.GetProperty("before").GetString().Should().Contain("90");
            entry.GetProperty("after").GetString().Should().Contain("60");
        }
        finally
        {
            (await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", JsonSerializer.Deserialize<object>(original.GetRawText()))).EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData(new[] { 20, 17 }, 90, 3, 400)] // not increasing
    [InlineData(new int[0], 90, 3, 400)]       // no brackets
    [InlineData(new[] { 12 }, 0, 3, 400)]      // guests kept 0 days
    [InlineData(new[] { 12 }, 90, 3, 30)]      // audit log under a year (§8.4)
    public async Task Bad_values_are_refused(int[] brackets, int guestDays, int years, int auditDays)
    {
        var res = await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", new
        {
            ageBrackets = brackets,
            retention = new { guestInviteDays = guestDays, deactivatedUserYears = years, auditLogDays = auditDays },
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static object Body(object devotion, object ai) => new
    {
        ageBrackets = (int[])[12, 17, 24, 35],
        retention = new { guestInviteDays = 90, deactivatedUserYears = 3, auditLogDays = 400 },
        devotion,
        ai,
    };

    private static readonly object SeedDevotion = new { dayOpens = "00:00", dayCloses = "23:59", dayReminder = "07:00", nightOpens = "18:00", nightCloses = "05:00", nightReminder = "20:00" };
    private static readonly object SeedAi = new { enabled = true, likelyHumanBelow = 40, likelyAiAbove = 75, membersSeeOwnScore = false };

    [Fact]
    public async Task Devotion_times_and_ai_settings_change_and_take_effect()
    {
        var original = await SuperAdmin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        original.GetProperty("devotion").GetProperty("nightCloses").GetString().Should().Be("05:00");
        original.GetProperty("ai").GetProperty("likelyAiAbove").GetInt32().Should().Be(75);
        var member = factory.ClientFor(ApiFactory.MemberAuth);
        (await member.GetFromJsonAsync<JsonElement>("/api/v1/me/devotions")).GetProperty("showsScores").GetBoolean().Should().BeFalse();
        try
        {
            var res = await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", Body(
                new { dayOpens = "04:00", dayCloses = "22:00", dayReminder = "08:30", nightOpens = "19:00", nightCloses = "02:00", nightReminder = "21:30" },
                new { enabled = true, likelyHumanBelow = 30, likelyAiAbove = 80, membersSeeOwnScore = true }));
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

            var now = await res.Content.ReadFromJsonAsync<JsonElement>();
            now.GetProperty("devotion").GetProperty("nightReminder").GetString().Should().Be("21:30");
            now.GetProperty("ai").GetProperty("likelyHumanBelow").GetInt32().Should().Be(30);
            (await member.GetFromJsonAsync<JsonElement>("/api/v1/me/devotions")).GetProperty("showsScores").GetBoolean()
                .Should().BeTrue("members now see their own score");
        }
        finally
        {
            (await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", JsonSerializer.Deserialize<object>(original.GetRawText()))).EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData("dayCloses", "25:00")]     // not a time
    [InlineData("dayCloses", "7:00")]      // not HH:mm
    [InlineData("dayOpens", "23:59")]      // Day closes before it opens
    [InlineData("nightCloses", "18:00")]   // Night opens and closes together
    [InlineData("dayReminder", "23:59:00")] // not HH:mm
    [InlineData("nightReminder", "01:00")] // after midnight: it would go out at opening time
    [InlineData("nightReminder", "17:00")] // before Night opens
    public async Task Bad_devotion_times_are_refused(string field, string value)
    {
        var devotion = JsonSerializer.SerializeToNode(SeedDevotion)!.AsObject();
        devotion[field] = value;
        (await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", Body(devotion, SeedAi))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(80, 70)] // human limit above the AI limit
    [InlineData(0, 75)]
    [InlineData(40, 100)]
    public async Task Bad_ai_limits_are_refused(int humanBelow, int aiAbove)
    {
        var ai = new { enabled = true, likelyHumanBelow = humanBelow, likelyAiAbove = aiAbove, membersSeeOwnScore = false };
        (await SuperAdmin.PatchAsJsonAsync("/api/v1/admin/settings", Body(SeedDevotion, ai))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settings_are_for_super_admins_only()
    {
        (await factory.ClientFor(ApiFactory.AdminAuth, "aal2").GetAsync("/api/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.ClientFor(ApiFactory.LeaderAuth).GetAsync("/api/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
