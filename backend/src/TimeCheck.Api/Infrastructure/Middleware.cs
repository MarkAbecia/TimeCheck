using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;
using TimeCheck.Api.Auth;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Domain.Common;
using TimeCheck.Infrastructure.Caching;

namespace TimeCheck.Api.Infrastructure;

/// <summary>X-Correlation-Id in/out, pushed to logs and the current trace (§8.5).</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var incoming = ctx.Request.Headers[Header].ToString();
        var id = incoming.Length is > 0 and <= 100 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        ctx.TraceIdentifier = id;
        ctx.Response.Headers[Header] = id;
        Activity.Current?.SetTag("correlation_id", id);
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(ctx);
        }
    }
}

public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}

/// <summary>API responses are JSON only: lock everything else down (§8.3).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        // The API docs UI (non-production only) needs scripts; everything else gets a deny-all CSP.
        if (!ctx.Request.Path.StartsWithSegments("/scalar"))
            h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        h.XFrameOptions = "DENY";
        return next(ctx);
    }
}

/// <summary>Per-endpoint rate-limit policy. Endpoints without one get <see cref="RateLimits.Default"/>.</summary>
public sealed record RateLimitPolicy(string Name, int Limit, TimeSpan Window);

public static class RateLimits
{
    public static readonly RateLimitPolicy Default = new("default", 300, TimeSpan.FromMinutes(1));
    public static readonly RateLimitPolicy Strict = new("strict", 10, TimeSpan.FromMinutes(1));
    public static readonly RateLimitPolicy Uploads = new("uploads", 30, TimeSpan.FromMinutes(10));
    public static readonly RateLimitPolicy Exports = new("exports", 10, TimeSpan.FromHours(1));

    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, RateLimitPolicy policy) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(policy);
}

/// <summary>Redis sliding window keyed by user (or IP when anonymous). Returns 429 + Retry-After.</summary>
public sealed class RateLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, RedisRateLimiter limiter)
    {
        var endpoint = ctx.GetEndpoint();
        if (endpoint is null || ctx.Request.Path.StartsWithSegments("/health")) { await next(ctx); return; }

        var policy = endpoint.Metadata.GetMetadata<RateLimitPolicy>() ?? RateLimits.Default;
        var who = ctx.User.FindFirstValue(AppClaims.AppUserId) is { } uid ? $"u:{uid}" : $"ip:{ctx.Connection.RemoteIpAddress}";
        var decision = await limiter.CheckAsync($"{policy.Name}:{who}", policy.Limit, policy.Window);
        if (!decision.Allowed)
        {
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests. Please wait and try again.").ExecuteAsync(ctx);
            return;
        }
        await next(ctx);
    }
}

/// <summary>Exceptions → RFC 7807 Problem Details. No stack traces or internals leak (§8.3).</summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException v => new ValidationProblemDetails(v.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "" : char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            { Status = 400, Title = "Some fields need attention." },
            DomainException d => new ProblemDetails { Status = 422, Title = d.Message, Extensions = { ["code"] = d.Code } },
            NotFoundException n => new ProblemDetails { Status = 404, Title = n.Message },
            ForbiddenException f => new ProblemDetails { Status = 403, Title = f.Message },
            ConflictException c => new ProblemDetails { Status = 409, Title = c.Message },
            DuplicateWarningException w => new ProblemDetails { Status = 409, Title = w.Message, Extensions = { ["code"] = "duplicate.warning", ["matches"] = w.Matches } },
            EventConflictWarningException w => new ProblemDetails { Status = 409, Title = w.Message, Extensions = { ["code"] = "event.conflict", ["conflicts"] = w.Conflicts } },
            // Two phones creating the same row at once (e.g. both marking an unmarked person): the loser retries with fresh data.
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } =>
                new ProblemDetails { Status = 409, Title = "Someone else changed this at the same time. Reload and try again.", Extensions = { ["code"] = "stale" } },
            DbUpdateConcurrencyException => new ProblemDetails { Status = 409, Title = "Someone else changed this at the same time. Reload and try again.", Extensions = { ["code"] = "stale" } },
            BadHttpRequestException b => new ProblemDetails { Status = b.StatusCode, Title = "The request could not be read." },
            _ => new ProblemDetails { Status = 500, Title = "Something went wrong on our side." },
        };
        if (problem.Status == 500) logger.LogError(exception, "Unhandled exception");

        ctx.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = problem, Exception = exception });
    }
}
