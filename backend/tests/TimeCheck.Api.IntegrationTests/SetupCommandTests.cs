using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using TimeCheck.Api.Infrastructure;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>The one-time command that creates a new church's first Super Admin (runbooks/database-roles.md).</summary>
[Collection(ApiTestGroup.Name)]
public class SetupCommandTests(ApiFactory factory)
{
    private async Task<(int Code, string Output)> Run(params string[] args)
    {
        var config = new ConfigurationBuilder().AddCommandLine(args).Build();
        var output = new StringWriter();
        var code = await SetupCommands.RunAsync(factory.Services, config, output);
        return (code, output.ToString());
    }

    [Fact]
    public async Task Creates_an_invited_super_admin_once_per_church()
    {
        var church = $"Setup Church {Guid.NewGuid():N}"[..24];
        await factory.Scalar<Guid>($"select public.create_church('{church}')");
        var email = $"first.{Guid.NewGuid():N}@example.test";

        var (code, output) = await Run("--email", email, "--first-name", "Grace", "--last-name", "Santos", "--gender", "female",
            "--birthdate", "1985-04-12", "--church", church);
        code.Should().Be(0, output);
        output.Should().Contain("Supabase emailed an invite");
        factory.AuthAdmin.Invited.Should().Contain(email);

        var row = await factory.Scalar<string>(
            $"select role || '|' || (auth_user_id is not null) || '|' || (birthdate_enc like 'v1:%') || '|' || must_change_password from public.app_users where email = '{email}'");
        row.Should().Be("SUPER_ADMIN|true|true|true", "encrypted with the service's keys, and onboarding sets the password");
        (await factory.Scalar<long>($"select count(*) from public.audit_logs where action = 'member.first_super_admin' and entity_id = (select id::text from public.app_users where email = '{email}')"))
            .Should().Be(1);

        var again = await Run("--email", $"second.{Guid.NewGuid():N}@example.test", "--first-name", "Ben", "--last-name", "Cruz", "--gender", "MALE",
            "--birthdate", "1990-01-01", "--church", church);
        again.Code.Should().Be(1);
        again.Output.Should().Contain("already has a Super Admin");
    }

    [Fact]
    public async Task Refuses_without_a_single_church_or_with_bad_input()
    {
        var ok = new[] { "--email", "x@example.test", "--first-name", "A", "--last-name", "B", "--gender", "MALE", "--birthdate", "1990-01-01" };

        (await Run(ok)).Output.Should().Contain("more than one church", "the test database has several");
        (await Run([.. ok, "--church", "No Such Church"])).Output.Should().Contain("No church named");

        var bad = await Run("--email", "not-an-email", "--first-name", "A", "--last-name", "B", "--gender", "MALE", "--birthdate", "1990-01-01");
        bad.Code.Should().Be(2);
        bad.Output.Should().Contain("That email isn't valid.").And.Contain("Usage:");
        (await Run([.. ok[..8], "--birthdate", "01/02/1990"])).Output.Should().Contain("Birthdate is yyyy-MM-dd.");
        (await Run([.. ok[..6], "--gender", "1", "--birthdate", "1990-01-01"])).Output.Should().Contain("Gender is MALE or FEMALE.");
        (await Run("--email", "x@example.test")).Output.Should().Contain("Every option except --church is required.");
    }
}
