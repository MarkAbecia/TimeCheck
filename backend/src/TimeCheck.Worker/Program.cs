using Hangfire;
using Hangfire.PostgreSql;
using Serilog;
using Serilog.Formatting.Compact;
using TimeCheck.Infrastructure;
using TimeCheck.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ITenantScope, AllChurchesTenantScope>();

// Hangfire needs a session-mode (or direct) connection, not the transaction-mode pooler.
var hangfireConnection = builder.Configuration.GetConnectionString("Hangfire")
    ?? throw new InvalidOperationException("ConnectionStrings:Hangfire is required.");
builder.Services.AddHangfire(c => c
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireConnection), new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));

// Recurring jobs (event generation, outbox delivery, AI detection, retention) are registered here in later phases.

await builder.Build().RunAsync();

/// <summary>The Worker processes every church; jobs scope their own queries by church_id.</summary>
internal sealed class AllChurchesTenantScope : ITenantScope
{
    public Guid? ChurchId => null;
}
