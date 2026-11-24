using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;

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

    public async Task<Guid> InviteUserAsync(string email, CancellationToken ct)
    {
        // GoTrue creates the user and sends the invite template, which links to /auth/confirm?type=invite.
        using var res = await http.PostAsJsonAsync("auth/v1/invite", new { email }, ct);
        if (res.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
            throw new ConflictException("An account with this email already exists.");
        if (!res.IsSuccessStatusCode)
        {
            logger.LogError("Supabase invite failed with {Status}", (int)res.StatusCode);
            res.EnsureSuccessStatusCode();
        }
        var user = await res.Content.ReadFromJsonAsync<InvitedUser>(ct);
        return user?.Id ?? throw new InvalidOperationException("Supabase invite returned no user id.");
    }

    public async Task DeleteUserAsync(Guid authUserId, CancellationToken ct)
    {
        using var res = await http.DeleteAsync($"auth/v1/admin/users/{authUserId}", ct);
        if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.NotFound)
            logger.LogError("Supabase delete user failed with {Status}", (int)res.StatusCode);
    }

    private sealed record InvitedUser(Guid Id);
}
