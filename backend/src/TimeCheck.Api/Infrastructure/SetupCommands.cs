using System.Globalization;
using System.Net.Mail;
using TimeCheck.Application.Common;
using TimeCheck.Application.Features.Admin;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Api.Infrastructure;

/// <summary>
/// One-off commands run from the API container instead of serving, e.g. in Render's shell:
/// <c>dotnet TimeCheck.Api.dll create-super-admin --email a@b.org --first-name Grace --last-name Santos --gender FEMALE --birthdate 1985-04-12</c>
/// (optional <c>--church "Name"</c>). Uses the service's own configuration, so the data is encrypted with its keys.
/// </summary>
public static class SetupCommands
{
    public const string CreateSuperAdmin = "create-super-admin";

    private const string Usage =
        "Usage: dotnet TimeCheck.Api.dll create-super-admin --email <email> --first-name <name> --last-name <name> " +
        "--gender MALE|FEMALE --birthdate yyyy-MM-dd [--church <name>]";

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration config, TextWriter output)
    {
        var input = Parse(config, out var problem);
        if (input is null)
        {
            await output.WriteLineAsync(problem);
            await output.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            var id = await scope.ServiceProvider.GetRequiredService<FirstSuperAdmin>().RunAsync(input, CancellationToken.None);
            await output.WriteLineAsync($"Created Super Admin {input.FirstName} {input.LastName} ({id}).");
            await output.WriteLineAsync($"Supabase emailed an invite to {input.Email}. They set a password from the link, then enrol a sign-in code.");
            return 0;
        }
        catch (ConflictException e)
        {
            await output.WriteLineAsync(e.Message);
            return 1;
        }
    }

    internal static FirstSuperAdminInput? Parse(IConfiguration config, out string problem)
    {
        string? Get(string key) => string.IsNullOrWhiteSpace(config[key]) ? null : config[key]!.Trim();
        var (email, first, last, gender, birthdate) = (Get("email"), Get("first-name"), Get("last-name"), Get("gender"), Get("birthdate"));

        problem = email is null || first is null || last is null || gender is null || birthdate is null ? "Every option except --church is required."
            : !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email ? "That email isn't valid."
            : first.Length > 100 || last.Length > 100 ? "Names are at most 100 characters."
            : !Enum.TryParse<Gender>(gender, ignoreCase: true, out _) || int.TryParse(gender, out _) ? "Gender is MALE or FEMALE."
            : !DateOnly.TryParseExact(birthdate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? "Birthdate is yyyy-MM-dd."
            : "";
        if (problem.Length > 0) return null;

        return new FirstSuperAdminInput(email!, first!, last!, Enum.Parse<Gender>(gender!, ignoreCase: true),
            DateOnly.ParseExact(birthdate!, "yyyy-MM-dd", CultureInfo.InvariantCulture), Get("church"));
    }
}
