using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.4 registration, §6.9 leader edits, §4.2 role/group rules, and the IDOR checks around them.</summary>
[Collection(ApiTestGroup.Name)]
public class MembersTests(ApiFactory factory)
{
    private static readonly Guid MemberAppId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid Leader2AppId = Guid.Parse("10000000-0000-0000-0000-000000000005");

    private static object NewMember(string? mobile = null, string first = "Ana", string? last = null, string birthdate = "1995-03-14",
        object? guardian = null, bool confirm = false) => new
        {
            // A unique surname, so tests sharing the database never look like duplicates of each other.
            firstName = first, lastName = last ?? $"Reyes{Guid.NewGuid():N}"[..14], gender = "FEMALE", birthdate,
            mobile = mobile ?? $"0917{Random.Shared.Next(1000000, 9999999)}",
            email = $"m{Guid.NewGuid():N}@example.test", guardian, confirmDuplicates = confirm,
        };

    private async Task<JsonElement> Register(object body, Guid? group = null)
    {
        var res = await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync($"/api/v1/groups/{group ?? ApiFactory.Group1}/members", body);
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Leader_registers_an_adult_into_own_group_and_an_invite_goes_out()
    {
        var body = NewMember();
        var member = await Register(body);
        var id = member.GetProperty("id").GetGuid();

        member.GetProperty("groupId").GetGuid().Should().Be(ApiFactory.Group1);
        member.GetProperty("status").GetString().Should().Be("NEW_MEMBER");
        member.GetProperty("isMinor").GetBoolean().Should().BeFalse();
        factory.AuthAdmin.Invited.Should().Contain(member.GetProperty("email").GetString());

        // The audit row says what happened without the person's details.
        var audit = await factory.Scalar<string>($"select after::text from audit_logs where action = 'member.register' and entity_id = '{id}'");
        audit.Should().NotBeNull().And.NotContain("@example.test").And.NotContain("0917");
        (await factory.Scalar<long>($"select count(*) from group_memberships where user_id = '{id}' and left_on is null")).Should().Be(1);

        var fetched = await factory.ClientFor(ApiFactory.LeaderAuth).GetFromJsonAsync<JsonElement>($"/api/v1/members/{id}");
        fetched.GetProperty("mobile").GetString().Should().StartWith("0917"); // decrypted for the leader
    }

    [Theory]
    [InlineData("leader-other-group", HttpStatusCode.NotFound)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    public async Task Registration_outside_your_group_is_refused(string who, HttpStatusCode expected)
    {
        var (auth, group) = who == "member" ? (ApiFactory.MemberAuth, ApiFactory.Group1) : (ApiFactory.LeaderAuth, ApiFactory.Group2);
        var res = await factory.ClientFor(auth).PostAsJsonAsync($"/api/v1/groups/{group}/members", NewMember());
        res.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task A_minor_needs_a_guardian_who_consented()
    {
        var birth = DateTime.UtcNow.AddYears(-15).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var client = factory.ClientFor(ApiFactory.LeaderAuth);

        (await client.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", NewMember(birthdate: birth))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members",
            NewMember(birthdate: birth, guardian: new { name = "Rosa Reyes", contact = "09171234567", consent = false }))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var minor = await Register(NewMember(birthdate: birth, guardian: new { name = "Rosa Reyes", contact = "09171234567", consent = true }));
        minor.GetProperty("isMinor").GetBoolean().Should().BeTrue();
        minor.GetProperty("guardianConsentAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Possible_duplicates_warn_until_confirmed_and_emails_must_be_unique()
    {
        var mobile = $"0918{Random.Shared.Next(1000000, 9999999)}";
        var first = await Register(NewMember(mobile: mobile, first: "Mara", last: $"Lim{Random.Shared.Next(1000)}"));
        var client = factory.ClientFor(ApiFactory.LeaderAuth);

        // Same number written the international way.
        var res = await client.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", NewMember(mobile: "+63" + mobile[1..]));
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("duplicate.warning");
        var match = problem.GetProperty("matches")[0];
        match.GetProperty("field").GetString().Should().Be("mobile");
        match.GetProperty("sameGroup").GetBoolean().Should().BeTrue();
        problem.GetRawText().Should().NotContain(first.GetProperty("email").GetString()!);

        await Register(NewMember(mobile: "+63" + mobile[1..], confirm: true));

        var sameEmail = new Dictionary<string, object?>
        {
            ["firstName"] = "Other", ["lastName"] = "Person", ["gender"] = "MALE", ["birthdate"] = "1990-01-01",
            ["mobile"] = "09990000000", ["email"] = first.GetProperty("email").GetString()!.ToUpperInvariant(),
        };
        (await client.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", sameEmail)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Leaders_reach_only_their_own_members()
    {
        var leader = factory.ClientFor(ApiFactory.LeaderAuth);
        (await leader.GetAsync($"/api/v1/members/{Leader2AppId}")).StatusCode.Should().Be(HttpStatusCode.NotFound); // group 2
        (await leader.GetAsync($"/api/v1/members/{ApiFactory.LeaderAppId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden); // self: use /me
        (await factory.ClientFor(ApiFactory.Leader2Auth).GetAsync($"/api/v1/members/{MemberAppId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.ClientFor(ApiFactory.MemberAuth).GetAsync($"/api/v1/members/{MemberAppId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.ClientFor(ApiFactory.OtherChurchAuth, "aal2").GetAsync($"/api/v1/members/{MemberAppId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Leader_edits_details_and_status_and_the_audit_lists_fields_not_values()
    {
        var id = (await Register(NewMember())).GetProperty("id").GetGuid();
        var leader = factory.ClientFor(ApiFactory.LeaderAuth);

        var update = await leader.PatchAsJsonAsync($"/api/v1/members/{id}", new
        {
            firstName = "Ana", lastName = "Reyes", gender = "FEMALE", birthdate = "1996-04-15", mobile = "09170000001", joinedOn = "2026-01-04",
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var audit = await factory.Scalar<string>($"select after::text from audit_logs where action = 'member.update' and entity_id = '{id}'");
        audit.Should().Contain("birthdate").And.Contain("mobile").And.NotContain("1996").And.NotContain("09170000001");

        var status = await leader.PostAsJsonAsync($"/api/v1/members/{id}/status", new { status = "DISCIPLE", reason = "Finished foundations" });
        (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("DISCIPLE");
        (await factory.Scalar<long>($"select count(*) from member_status_changes where user_id = '{id}' and to_status = 'DISCIPLE'")).Should().Be(1);
    }

    [Fact]
    public async Task Admins_with_mfa_change_roles_and_the_member_gets_a_notification()
    {
        var id = (await Register(NewMember())).GetProperty("id").GetGuid();
        var body = new { role = "LEADER", reason = "Ready to lead" };

        (await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync($"/api/v1/members/{id}/role", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.ClientFor(ApiFactory.AdminAuth, "aal1").PostAsJsonAsync($"/api/v1/members/{id}/role", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var res = await factory.ClientFor(ApiFactory.AdminAuth, "aal2").PostAsJsonAsync($"/api/v1/members/{id}/role", body);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await factory.Scalar<long>($"select count(*) from role_changes where user_id = '{id}' and to_role = 'LEADER'")).Should().Be(1);
        (await factory.Scalar<string>($"select type from notifications where user_id = '{id}'")).Should().Be("role.changed");

        // Admin roles are for super admins only.
        (await factory.ClientFor(ApiFactory.AdminAuth, "aal2").PostAsJsonAsync($"/api/v1/members/{id}/role", new { role = "ADMIN" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_leader_who_still_leads_a_group_cannot_be_demoted()
    {
        var res = await factory.ClientFor(ApiFactory.AdminAuth, "aal2").PostAsJsonAsync($"/api/v1/members/{Leader2AppId}/role", new { role = "MEMBER" });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_moves_a_member_and_history_is_kept()
    {
        var id = (await Register(NewMember())).GetProperty("id").GetGuid();
        var admin = factory.ClientFor(ApiFactory.AdminAuth, "aal2");

        (await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync($"/api/v1/members/{id}/move", new { groupId = ApiFactory.Group2 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var res = await admin.PostAsJsonAsync($"/api/v1/members/{id}/move", new { groupId = ApiFactory.Group2 });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        (await factory.Scalar<long>($"select count(*) from group_memberships where user_id = '{id}'")).Should().Be(2);
        (await factory.Scalar<Guid>($"select group_id from group_memberships where user_id = '{id}' and left_on is null")).Should().Be(ApiFactory.Group2);
        (await factory.ClientFor(ApiFactory.LeaderAuth).GetAsync($"/api/v1/members/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

[Collection(ApiTestGroup.Name)]
public class GroupManagementTests(ApiFactory factory)
{
    [Fact]
    public async Task Leaders_list_their_groups_admins_list_all_members_none()
    {
        var mine = await factory.ClientFor(ApiFactory.LeaderAuth).GetFromJsonAsync<JsonElement>("/api/v1/groups");
        mine.EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).Should().Equal(ApiFactory.Group1);

        var all = await factory.ClientFor(ApiFactory.AdminAuth, "aal2").GetFromJsonAsync<JsonElement>("/api/v1/groups");
        all.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        (await factory.ClientFor(ApiFactory.MemberAuth).GetAsync("/api/v1/groups")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_creates_renames_and_archives_a_group()
    {
        var admin = factory.ClientFor(ApiFactory.AdminAuth, "aal2");
        var name = $"Youth {Random.Shared.Next(100000)}";

        var created = await admin.PostAsJsonAsync("/api/v1/groups", new { name });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync("/api/v1/groups", new { name = name.ToUpperInvariant() })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PatchAsJsonAsync($"/api/v1/groups/{id}", new { name, leaderId = Guid.Parse("10000000-0000-0000-0000-000000000004") }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest); // a plain member can't lead
        (await admin.PatchAsJsonAsync($"/api/v1/groups/{id}", new { name = name + " B", leaderId = ApiFactory.LeaderAppId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/v1/groups/{ApiFactory.Group1}/archive", null)).StatusCode.Should().Be(HttpStatusCode.Conflict); // has members
        (await admin.PostAsync($"/api/v1/groups/{id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.ClientFor(ApiFactory.LeaderAuth).PostAsJsonAsync("/api/v1/groups", new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
