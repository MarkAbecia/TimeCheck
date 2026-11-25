using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TimeCheck.Application.Features.Privacy;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>§8.4 and §6.10: deactivation, a person's data export, erasure that keeps attendance counts, and retention.</summary>
[Collection(ApiTestGroup.Name)]
public class PrivacyTests(ApiFactory factory)
{
    private const string LeaderApp = "10000000-0000-0000-0000-000000000003";
    private HttpClient Admin => factory.ClientFor(ApiFactory.AdminAuth, "aal2");
    private HttpClient Leader => factory.ClientFor(ApiFactory.LeaderAuth);

    /// <summary>A new member of Group 1 with an attendance mark, a devotion and a notification. Returns (id, authUserId).</summary>
    private async Task<(Guid Id, Guid Auth)> NewMemberWithHistory()
    {
        var res = await Leader.PostAsJsonAsync($"/api/v1/groups/{ApiFactory.Group1}/members", new
        {
            firstName = "Erin", lastName = $"Cruz{Guid.NewGuid():N}"[..14], gender = "FEMALE", birthdate = "1998-07-21",
            mobile = $"0917{Random.Shared.Next(1000000, 9999999)}", email = $"p{Guid.NewGuid():N}@example.test", address = "12 Mabini St",
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await factory.Scalar<int>($"""
            with e as (
              insert into events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on)
              select t.church_id, t.id, '{ApiFactory.Group1}', 'Privacy test', '2026-02-01T11:00:00Z', '2026-02-01T13:00:00Z', '2026-02-01'
              from event_types t join app_users u on u.church_id = t.church_id where t.code = 'CELL_MEETING' and u.id = '{id}'
              returning id, church_id)
            insert into attendance (church_id, event_id, user_id, status) select church_id, id, '{id}', 'PRESENT' from e;
            insert into devotions (church_id, user_id, devotion_date, slot, text_content, content_hash)
              select church_id, id, '2026-02-01', 'DAY', 'Psalm 23 reflection', repeat('a', 64) from app_users where id = '{id}';
            insert into notifications (church_id, user_id, type) select church_id, id, 'event.changed' from app_users where id = '{id}';
            select 1
            """);
        return (id, (await factory.Scalar<Guid>($"select auth_user_id from app_users where id = '{id}'")));
    }

    [Fact]
    public async Task Admin_exports_one_persons_data_and_a_leader_cannot()
    {
        var (id, _) = await NewMemberWithHistory();
        (await Leader.GetAsync($"/api/v1/members/{id}/data-export")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var export = await Admin.GetFromJsonAsync<JsonElement>($"/api/v1/members/{id}/data-export");
        export.GetProperty("profile").GetProperty("firstName").GetString().Should().Be("Erin");
        export.GetProperty("profile").GetProperty("address").GetString().Should().Be("12 Mabini St", "decrypted for the person's own copy");
        export.GetProperty("attendance").GetArrayLength().Should().Be(1);
        export.GetProperty("devotions")[0].GetProperty("text").GetString().Should().Be("Psalm 23 reflection");
        export.GetProperty("notifications").GetArrayLength().Should().Be(1);
        (await factory.Scalar<long>($"select count(*) from audit_logs where action = 'member.data_export' and entity_id = '{id}'")).Should().Be(1);
    }

    [Fact]
    public async Task Erasure_needs_deactivation_first_then_removes_personal_data_but_keeps_attendance()
    {
        var (id, auth) = await NewMemberWithHistory();
        (await Admin.PostAsync($"/api/v1/members/{id}/erase", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Leader.PostAsJsonAsync($"/api/v1/members/{id}/active", new { active = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var off = await Admin.PostAsJsonAsync($"/api/v1/members/{id}/active", new { active = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        (await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        (await Admin.PostAsync($"/api/v1/members/{id}/erase", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await factory.Scalar<string>($"""
            select concat_ws('|', first_name, last_name, email, mobile_enc is null, address_enc is null, auth_user_id is null, deleted_at is not null)
            from app_users where id = '{id}'
            """)).Should().Be($"Removed|member|erased-{id:N}@erased.invalid|t|t|t|t");
        (await factory.Scalar<long>($"select count(*) from attendance where user_id = '{id}'")).Should().Be(1, "attendance counts stay");
        (await factory.Scalar<string>($"select text_content from devotions where user_id = '{id}'")).Should().BeEmpty();
        (await factory.Scalar<long>($"select count(*) from notifications where user_id = '{id}'")).Should().Be(0);
        (await factory.Scalar<long>($"select count(*) from group_memberships where user_id = '{id}' and left_on is null")).Should().Be(0);
        factory.AuthAdmin.Deleted.Should().Contain(auth);
        (await factory.Scalar<string>($"select after::text from audit_logs where action = 'member.erase' and entity_id = '{id}'"))
            .Should().NotContain("Erin", "the audit row records the erasure, not the person");
        (await Admin.GetAsync($"/api/v1/members/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_group_leader_cant_be_deactivated_while_leading()
    {
        var res = await Admin.PostAsJsonAsync($"/api/v1/members/{LeaderApp}/active", new { active = false });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Choose another leader");
    }

    [Fact]
    public async Task Retention_erases_long_deactivated_people_and_keeps_a_year_of_audit_log()
    {
        var (gone, _) = await NewMemberWithHistory();
        var (recent, _) = await NewMemberWithHistory();
        await factory.Scalar<int>($"""
            update app_users set deactivated_at = now() - interval '4 years' where id = '{gone}';
            update app_users set deactivated_at = now() - interval '1 year' where id = '{recent}';
            insert into audit_logs (church_id, action, entity_id, occurred_at)
              select church_id, 'test.old', '{gone}', now() - interval '500 days' from app_users where id = '{gone}';
            insert into audit_logs (church_id, action, entity_id, occurred_at)
              select church_id, 'test.recent', '{gone}', now() - interval '370 days' from app_users where id = '{gone}';
            select 1
            """);

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);

        (await factory.Scalar<bool>($"select deleted_at is not null from app_users where id = '{gone}'")).Should().BeTrue();
        (await factory.Scalar<bool>($"select deleted_at is null from app_users where id = '{recent}'")).Should().BeTrue();
        (await factory.Scalar<long>($"select count(*) from audit_logs where entity_id = '{gone}' and action like 'test.%'")).Should().Be(1, "only rows past 400 days go");
    }

    [Fact]
    public async Task The_database_refuses_to_purge_audit_rows_under_a_year_old()
    {
        var act = () => factory.Scalar<int>("select public.purge_audit_logs(id, now() - interval '30 days') from churches limit 1");
        await act.Should().ThrowAsync<Exception>().WithMessage("*at least a year*");
    }

    [Theory]
    [InlineData("{}", 3, 400)]
    [InlineData("""{"retention":{"deactivatedUserYears":5,"auditLogDays":30}}""", 5, 365)]
    [InlineData("""{"retention":{"deactivatedUserYears":0,"auditLogDays":800}}""", 3, 800)]
    public void Retention_settings_have_safe_floors(string settings, int years, int days) =>
        RetentionJob.Read(settings).Should().Be(new RetentionJob.Policy(years, days));
}
