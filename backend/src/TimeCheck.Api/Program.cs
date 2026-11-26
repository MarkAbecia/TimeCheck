using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using TimeCheck.Api.Auth;
using TimeCheck.Api.Endpoints;
using TimeCheck.Api.Infrastructure;
using TimeCheck.Application.Abstractions;
using TimeCheck.Application.Common;
using TimeCheck.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Destructure.With<PiiMaskingPolicy>()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTimeCheckAuth(builder.Configuration);
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; // reject unknown fields (§8.3)
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["correlationId"] = c.HttpContext.TraceIdentifier;
    c.ProblemDetails.Extensions.Remove("traceId");
});
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // Render's proxy sits in front; it is the only way in.
    o.KnownProxies.Clear();
});

builder.Services.AddHealthChecks()
    .AddNpgSql(sp => sp.GetRequiredService<Npgsql.NpgsqlDataSource>(), name: "postgres", tags: ["ready"])
    .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("timecheck-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporterIfConfigured(builder.Configuration))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporterIfConfigured(builder.Configuration));

builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info = new OpenApiInfo { Title = "TimeCheck API", Version = "v1" };
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    doc.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Supabase access token (forwarded by the Next.js BFF).",
    };
    doc.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", doc)] = [] }];
    return Task.CompletedTask;
}));

var app = builder.Build();

if (args.FirstOrDefault() == SetupCommands.CreateSuperAdmin)
{
    Environment.ExitCode = await SetupCommands.RunAsync(app.Services, app.Configuration, Console.Out);
    return;
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapTimeCheckEndpoints();

await app.RunAsync();

public partial class Program;

internal static class OtelExtensions
{
    public static TracerProviderBuilder AddOtlpExporterIfConfigured(this TracerProviderBuilder b, IConfiguration c) =>
        string.IsNullOrEmpty(c["OTEL_EXPORTER_OTLP_ENDPOINT"]) ? b : b.AddOtlpExporter();

    public static MeterProviderBuilder AddOtlpExporterIfConfigured(this MeterProviderBuilder b, IConfiguration c) =>
        string.IsNullOrEmpty(c["OTEL_EXPORTER_OTLP_ENDPOINT"]) ? b : b.AddOtlpExporter();
}
