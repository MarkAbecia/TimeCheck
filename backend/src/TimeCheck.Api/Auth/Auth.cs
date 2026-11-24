using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Enums;
using TimeCheck.Infrastructure.Persistence;

namespace TimeCheck.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>Supabase project URL, e.g. https://xyz.supabase.co. Issuer is {Url}/auth/v1.</summary>
    public string SupabaseUrl { get; set; } = "";
    public string Audience { get; set; } = "authenticated";
    /// <summary>Admins and Super Admins must present an aal2 (MFA) session (§6.1).</summary>
    public bool RequireAdminMfa { get; set; } = true;
    /// <summary>Privacy notice version users must accept (§8.4). Bump it when the notice changes; everyone re-consents.</summary>
    public string PrivacyConsentVersion { get; set; } = "2026-11";
}

public sealed class ConfiguredAccountPolicy(Microsoft.Extensions.Options.IOptions<AuthOptions> options) : IAccountPolicy
{
    public string PrivacyConsentVersion => options.Value.PrivacyConsentVersion;
    public bool RequireAdminMfa => options.Value.RequireAdminMfa;
}

public static class Policies
{
    /// <summary>Signed in, but onboarding (password change, privacy consent) may still be owed. Only /me uses it.</summary>
    public const string SignedIn = "signed_in";
    public const string Leader = "leader";
    public const string Admin = "admin";
    public const string SuperAdmin = "super_admin";
}

internal static class AuthItems
{
    public const string CurrentUser = "tc.current_user";
}

public static class AuthSetup
{
    public static IServiceCollection AddTimeCheckAuth(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
        var issuer = $"{options.SupabaseUrl.TrimEnd('/')}/auth/v1";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            // Supabase publishes JWKS (asymmetric signing keys) under the issuer's discovery document.
            o.MetadataAddress = $"{issuer}/.well-known/openid-configuration";
            o.RequireHttpsMetadata = issuer.StartsWith("https://", StringComparison.Ordinal);
            o.TokenValidationParameters.ValidIssuer = issuer;
            o.TokenValidationParameters.ValidAudience = options.Audience;
            o.TokenValidationParameters.ValidateLifetime = true;
            o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            o.TokenValidationParameters.NameClaimType = "sub";
            o.Events = new JwtBearerEvents { OnTokenValidated = AttachAppUser };
        });

        // Everything except /me also requires finished onboarding, so a user who still owes the forced password
        // change or the privacy consent can reach nothing else, whatever the client does (§6.1, §8.4).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(AppClaims.AppUserId)
                .AddRequirements(new OnboardedRequirement()).Build())
            .AddPolicy(Policies.SignedIn, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.AppUserId))
            .AddPolicy(Policies.Leader, p => p.RequireAuthenticatedUser().AddRequirements(new OnboardedRequirement(), new RoleRequirement(UserRole.LEADER)))
            .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new OnboardedRequirement(), new RoleRequirement(UserRole.ADMIN)))
            .AddPolicy(Policies.SuperAdmin, p => p.RequireAuthenticatedUser().AddRequirements(new OnboardedRequirement(), new RoleRequirement(UserRole.SUPER_ADMIN)));
        services.AddSingleton<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, OnboardedRequirementHandler>();
        services.AddSingleton<IAccountPolicy, ConfiguredAccountPolicy>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ITenantScope, HttpTenantScope>();
        return services;
    }

    /// <summary>Role/church/group come from app_users, never from the token (§8.1).</summary>
    private static async Task AttachAppUser(TokenValidatedContext ctx)
    {
        var sub = ctx.Principal?.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var authUserId)) { ctx.Fail("Missing subject."); return; }

        var directory = ctx.HttpContext.RequestServices.GetRequiredService<IUserDirectory>();
        var info = await directory.FindByAuthUserIdAsync(authUserId, ctx.HttpContext.RequestAborted);
        if (info is null || !info.IsActive) { ctx.Fail("Unknown or deactivated user."); return; }

        ctx.HttpContext.Items[AuthItems.CurrentUser] = info;
        var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
        identity.AddClaim(new Claim(AppClaims.AppUserId, info.AppUserId.ToString()));
        identity.AddClaim(new Claim(AppClaims.Role, info.Role.ToString()));
    }
}

public static class AppClaims
{
    public const string AppUserId = "tc_app_user_id";
    public const string Role = "tc_role";
}

public sealed record RoleRequirement(UserRole Minimum) : IAuthorizationRequirement;

public sealed class RoleRequirementHandler(Microsoft.Extensions.Options.IOptions<AuthOptions> options) : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        var info = (context.Resource as HttpContext)?.Items[AuthItems.CurrentUser] as CurrentUserInfo;
        if (info is null || !info.Role.IsAtLeast(requirement.Minimum)) return Task.CompletedTask;

        var needsMfa = requirement.Minimum.IsAtLeast(UserRole.ADMIN) && options.Value.RequireAdminMfa;
        if (needsMfa && context.User.FindFirstValue("aal") != "aal2") return Task.CompletedTask;

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed record OnboardedRequirement : IAuthorizationRequirement;

public sealed class OnboardedRequirementHandler(IAccountPolicy policy) : AuthorizationHandler<OnboardedRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OnboardedRequirement requirement)
    {
        if ((context.Resource as HttpContext)?.Items[AuthItems.CurrentUser] is CurrentUserInfo info && info.IsComplete(policy))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public CurrentUserInfo Info =>
        accessor.HttpContext?.Items[AuthItems.CurrentUser] as CurrentUserInfo
        ?? throw new InvalidOperationException("No authenticated user.");
}

/// <summary>Anonymous requests get an empty tenant so tenant-filtered queries match nothing.</summary>
public sealed class HttpTenantScope(IHttpContextAccessor accessor) : ITenantScope
{
    public Guid? ChurchId => (accessor.HttpContext?.Items[AuthItems.CurrentUser] as CurrentUserInfo)?.ChurchId ?? Guid.Empty;
}
