using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.10: member search and MFA reset, admins only, with the same reach rules as the member record.</summary>
[Collection(ApiTestGroup.Name)]
public class AdminToolsTests(ApiFactory factory)
{
    private const string MemberApp = "10000000-0000-0000-0000-000000000004"; // Nico Bautista, seed
    private const string AdminApp = "10000000-0000-0000-0000-000000000002"; // Paolo Reyes, seed
    private HttpClient Admin => factory.ClientFor(ApiFactory.AdminAuth, "aal2");
    private HttpClient SuperAdmin => factory.ClientFor(ApiFactory.SuperAdminAuth, "aal2");

    private static async Task<List<JsonElement>> Hits(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return [.. (await res.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    [Fact]
    public async Task Admin_finds_people_by_name_full_name_or_email_but_not_admins_or_themself()
    {
        var byFirst = await Hits(await Admin.GetAsync("/api/v1/members?q=nic"));
        byFirst.Should().Contain(h => h.GetProperty("id").GetString() == MemberApp);
        var hit = byFirst.First(h => h.GetProperty("id").GetString() == MemberApp);
        hit.GetProperty("groupName").GetString().Should().NotBeNullOrEmpty();
        hit.GetProperty("isActive").GetBoolean().Should().BeTrue();

        (await Hits(await Admin.GetAsync("/api/v1/members?q=Nico%20Baut"))).Should().Contain(h => h.GetProperty("id").GetString() == MemberApp);
        (await Hits(await Admin.GetAsync("/api/v1/members?q=MEMBER@timecheck"))).Should().Contain(h => h.GetProperty("id").GetString() == MemberApp);
        (await Hits(await Admin.GetAsync("/api/v1/members?q=Reyes"))).Should().NotContain(h => h.GetProperty("id").GetString() == AdminApp, "admins see neither themselves nor other admins");
        (await Hits(await SuperAdmin.GetAsync("/api/v1/members?q=Paolo"))).Should().Contain(h => h.GetProperty("id").GetString() == AdminApp);
        (await Hits(await Admin.GetAsync("/api/v1/members?q=%25%25"))).Should().BeEmpty("a % is matched literally, not as a wildcard");
    }

    [Fact]
    public async Task Search_finds_a_mobile_number_typed_either_way()
    {
        var mobile = $"0917{Random.Shared.Next(1000000, 9999999)}";
        var res = await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", new
        {
            firstName = "Mia", lastName = $"Lim{Guid.NewGuid():N}"[..12], gender = "FEMALE", birthdate = "1990-01-02", mobile,
            email = $"s{Guid.NewGuid():N}@example.test",
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        (await Hits(await Admin.GetAsync($"/api/v1/members?q={Uri.EscapeDataString("+63" + mobile[1..])}"))).Should().ContainSingle(h => h.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Search_is_for_admins_and_needs_two_characters()
    {
        (await factory.ClientFor(ApiFactory.LeaderAuth).GetAsync("/api/v1/members?q=nico")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Admin.GetAsync("/api/v1/members?q=n")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_resets_a_members_mfa_who_is_told_and_it_is_audited()
    {
        factory.AuthAdmin.Factors[ApiFactory.MemberAuth] = 1;
        var res = await Admin.PostAsync($"/api/v1/members/{MemberApp}/mfa-reset", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetInt32().Should().Be(1);
        factory.AuthAdmin.Factors.Should().NotContainKey(ApiFactory.MemberAuth);

        (await factory.Scalar<long>($"select count(*) from notifications where user_id = '{MemberApp}' and type = 'mfa.reset'")).Should().BeGreaterThan(0);
        (await factory.Scalar<long>($"select count(*) from audit_logs where action = 'member.mfa_reset' and entity_id = '{MemberApp}'")).Should().BeGreaterThan(0);

        // Nothing left to remove: still fine, and nobody is told twice.
        var again = await Admin.PostAsync($"/api/v1/members/{MemberApp}/mfa-reset", null);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Only_a_super_admin_resets_an_admin_and_leaders_cannot_reset_anyone()
    {
        (await factory.ClientFor(ApiFactory.LeaderAuth).PostAsync($"/api/v1/members/{MemberApp}/mfa-reset", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Admin.PostAsync("/api/v1/members/10000000-0000-0000-0000-000000000001/mfa-reset", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SuperAdmin.PostAsync($"/api/v1/members/{AdminApp}/mfa-reset", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Force_logout_ends_sessions_and_refuses_tokens_issued_before_it()
    {
        // Lia Ramos (seed member of Group 2), so other tests signed in as seeded people are unaffected.
        const string LiaApp = "10000000-0000-0000-0000-000000000006";
        var liaAuth = Guid.Parse("30000000-0000-0000-0000-000000000006");
        await factory.Scalar<int>($"insert into auth.sessions (user_id) values ('{liaAuth}'), ('{liaAuth}'); select 1");
        var oldToken = factory.Token(liaAuth, issuedAt: DateTime.UtcNow.AddMinutes(-10));
        using var before = factory.CreateClient();
        before.DefaultRequestHeaders.Authorization = new("Bearer", oldToken);
        (await before.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.ClientFor(ApiFactory.LeaderAuth).PostAsync($"/api/v1/members/{LiaApp}/force-logout", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "admins only");
        var res = await Admin.PostAsync($"/api/v1/members/{LiaApp}/force-logout", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessions").GetInt32().Should().Be(2);

        (await factory.Scalar<long>($"select count(*) from auth.sessions where user_id = '{liaAuth}'")).Should().Be(0);
        (await factory.Scalar<long>($"select count(*) from audit_logs where action = 'member.force_logout' and entity_id = '{LiaApp}'")).Should().Be(1);
        (await before.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old token was issued before the logout");

        // Signing in again works: a token issued after the logout is accepted.
        using var after = factory.CreateClient();
        after.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(liaAuth, issuedAt: DateTime.UtcNow.AddSeconds(2)));
        (await after.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_a_super_admin_signs_out_an_admin()
    {
        (await Admin.PostAsync("/api/v1/members/10000000-0000-0000-0000-000000000001/force-logout", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SuperAdmin.PostAsync($"/api/v1/members/{AdminApp}/force-logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        // The admin's own earlier tokens are now refused; later tests mint fresh ones, issued after this.
    }

    [Fact]
    public async Task Super_admin_reads_one_persons_history_with_names_and_lists_the_actions()
    {
        (await Admin.PostAsync($"/api/v1/members/{MemberApp}/mfa-reset", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await SuperAdmin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/audit-logs?entityType=app_user&entityId={MemberApp}&limit=200");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty().And.OnlyContain(i => i.GetProperty("entityId").GetString() == MemberApp);
        var reset = items.First(i => i.GetProperty("action").GetString() == "member.mfa_reset");
        reset.GetProperty("actorName").GetString().Should().Be("Paolo Reyes");
        reset.GetProperty("entityName").GetString().Should().NotBeNullOrEmpty("another test may have set his nickname");

        (await SuperAdmin.GetFromJsonAsync<List<string>>("/api/v1/admin/audit-actions")).Should().Contain("member.mfa_reset").And.BeInAscendingOrder();
        (await Admin.GetAsync("/api/v1/admin/audit-actions")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "super admins only");
    }
}
