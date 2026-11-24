using Hangfire;
using Hangfire.PostgreSql;
using Serilog;
using Serilog.Formatting.Compact;
using TimeCheck.Application.Features.Events;
using TimeCheck.Infrastructure;
using TimeCheck.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ITenantScope, AllChurchesTenantScope>();
builder.Services.AddScoped<EventGenerator>();

// Hangfire needs a session-mode (or direct) connection, not the transaction-mode pooler.
var hangfireConnection = builder.Configuration.GetConnectionString("Hangfire")
    ?? throw new InvalidOperationException("ConnectionStrings:Hangfire is required.");
builder.Services.AddHangfire(c => c
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireConnection), new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));

var host = builder.Build();

// Recurring jobs. Outbox delivery, AI detection and retention join in later phases.
// Event generation: shortly after midnight Manila time, and once at start-up so a fresh deploy has its next 14 days.
var jobs = host.Services.GetRequiredService<IRecurringJobManager>();
jobs.AddOrUpdate<EventGenerator>("generate-events", g => g.GenerateAsync(EventGenerator.DaysAhead, CancellationToken.None), "10 0 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila") });
jobs.Trigger("generate-events");

await host.RunAsync();

/// <summary>The Worker processes every church; jobs scope their own queries by church_id.</summary>
internal sealed class AllChurchesTenantScope : ITenantScope
{
    public Guid? ChurchId => null;
}
