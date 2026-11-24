using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§6.1 forced password change, §8.4 privacy consent, enforced by the API whatever the client does.</summary>
[Collection(ApiTestGroup.Name)]
public class OnboardingTests(ApiFactory factory)
{
    [Fact]
    public async Task Newcomer_reaches_only_me_until_password_and_consent_are_done()
    {
        var client = factory.ClientFor(ApiFactory.NewcomerAuth);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        me.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        me.GetProperty("privacyConsentVersion").ValueKind.Should().Be(JsonValueKind.Null);
        me.GetProperty("requiredPrivacyConsentVersion").GetString().Should().Be("2026-11");
        me.GetProperty("churchName").GetString().Should().NotBeNullOrEmpty();
        (await client.GetAsync($"/api/v1/groups/{ApiFactory.Group1}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Weak password is rejected before Supabase is called.
        (await client.PostAsJsonAsync("/api/v1/me/password", new { newPassword = "short" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        factory.AuthAdmin.PasswordSetFor.Should().NotContain(ApiFactory.NewcomerAuth);

        var changed = await client.PostAsJsonAsync("/api/v1/me/password", new { newPassword = "blessed-day-2026" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        factory.AuthAdmin.PasswordSetFor.Should().Contain(ApiFactory.NewcomerAuth);

        // Still blocked: consent is owed. An outdated version is refused.
        (await client.GetAsync($"/api/v1/groups/{ApiFactory.Group1}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/me/privacy-consent", new { version = "2020-01" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/v1/me/privacy-consent", new { version = "2026-11" })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Onboarded: normal authorization applies (a member of no group gets 404 for someone else's group).
        (await client.GetAsync($"/api/v1/groups/{ApiFactory.Group1}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var audit = await factory.Scalar<long>(
            "select count(*) from public.audit_logs where actor_id = '10000000-0000-0000-0000-000000000077' " +
            "and action in ('auth.password_change', 'privacy.consent')");
        audit.Should().Be(2);
        var leaked = await factory.Scalar<long>("select count(*) from public.audit_logs where after::text like '%blessed-day%'");
        leaked.Should().Be(0);
    }

    [Fact]
    public async Task Me_tells_admins_that_mfa_is_required()
    {
        var admin = await factory.ClientFor(ApiFactory.AdminAuth).GetFromJsonAsync<JsonElement>("/api/v1/me");
        var member = await factory.ClientFor(ApiFactory.MemberAuth).GetFromJsonAsync<JsonElement>("/api/v1/me");
        admin.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
        member.GetProperty("mfaRequired").GetBoolean().Should().BeFalse();
        member.GetProperty("groupName").GetString().Should().NotBeNullOrEmpty();
    }
}
