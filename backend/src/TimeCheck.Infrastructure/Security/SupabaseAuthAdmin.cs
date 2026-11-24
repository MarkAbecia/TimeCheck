using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using TimeCheck.Application.Abstractions;

namespace TimeCheck.Infrastructure.Security;

/// <summary>
/// Supabase Auth admin API (GoTrue /admin). Authenticated with the project's secret (service role) key, which exists
/// only in the API/worker environment as Auth:ServiceRoleKey (§8.1). Sent as <c>apikey</c> only: the gateway turns it
/// into the service-role credential, and the new sb_secret_ keys are not JWTs.
/// </summary>
public sealed class SupabaseAuthAdmin(HttpClient http, ILogger<SupabaseAuthAdmin> logger) : IAuthAdmin
{
    public const string HttpClientName = "supabase-auth-admin";

    public async Task SetPasswordAsync(Guid authUserId, string newPassword, CancellationToken ct)
    {
        using var res = await http.PutAsJsonAsync($"auth/v1/admin/users/{authUserId}", new { password = newPassword }, ct);
        if (res.IsSuccessStatusCode) return;

        if (res.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            // Weak or reused password per Supabase's own policy. The body never contains the password.
            throw new ValidationException([new ValidationFailure("newPassword", "Choose a stronger password.")]);
        }

        logger.LogError("Supabase admin password update failed with {Status}", (int)res.StatusCode);
        res.EnsureSuccessStatusCode();
    }
}
