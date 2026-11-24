using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.NameTranslation;
using StackExchange.Redis;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Enums;
using TimeCheck.Infrastructure.Caching;
using TimeCheck.Infrastructure.Persistence;
using TimeCheck.Infrastructure.Security;

namespace TimeCheck.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<EncryptionOptions>().Bind(config.GetSection(EncryptionOptions.Section)).ValidateOnStart();
        services.AddSingleton<FieldEncryptor>();
        services.AddSingleton<IBlindIndexer, BlindIndexer>();
        services.AddSingleton(TimeProvider.System);

        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
        services.AddSingleton(_ => BuildDataSource(connectionString));
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql =>
            {
                npgsql.MapEnum<UserRole>("user_role", "public", NullTranslator);
                npgsql.MapEnum<MemberStatus>("member_status", "public", NullTranslator);
                npgsql.MapEnum<Gender>("gender", "public", NullTranslator);
                npgsql.MapEnum<EventCategory>("event_category", "public", NullTranslator);
                npgsql.MapEnum<EventCadence>("event_cadence", "public", NullTranslator);
                npgsql.MapEnum<EventScope>("event_scope", "public", NullTranslator);
                npgsql.MapEnum<AttendanceStatus>("attendance_status", "public", NullTranslator);
                npgsql.MapEnum<DevotionSlot>("devotion_slot", "public", NullTranslator);
                npgsql.MapEnum<AiStatus>("ai_status", "public", NullTranslator);
                npgsql.MapEnum<InviteStatus>("invite_status", "public", NullTranslator);
                npgsql.MapEnum<ReportJobStatus>("report_job_status", "public", NullTranslator);
            })
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(RedisConfiguration(config)));
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton<RedisRateLimiter>();
        return services;
    }

    private static readonly NpgsqlNullNameTranslator NullTranslator = new();

    /// <summary>
    /// The API goes through Supavisor in transaction mode, which can't keep server-side prepared statements:
    /// put "Max Auto Prepare=0;No Reset On Close=true" in the connection string there (ADR 0001).
    /// </summary>
    private static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var b = new NpgsqlDataSourceBuilder(connectionString);
        // Enums must be mapped on the data source too, not only in the EF model.
        b.MapEnum<UserRole>("public.user_role", NullTranslator);
        b.MapEnum<MemberStatus>("public.member_status", NullTranslator);
        b.MapEnum<Gender>("public.gender", NullTranslator);
        b.MapEnum<EventCategory>("public.event_category", NullTranslator);
        b.MapEnum<EventCadence>("public.event_cadence", NullTranslator);
        b.MapEnum<EventScope>("public.event_scope", NullTranslator);
        b.MapEnum<AttendanceStatus>("public.attendance_status", NullTranslator);
        b.MapEnum<DevotionSlot>("public.devotion_slot", NullTranslator);
        b.MapEnum<AiStatus>("public.ai_status", NullTranslator);
        b.MapEnum<InviteStatus>("public.invite_status", NullTranslator);
        b.MapEnum<ReportJobStatus>("public.report_job_status", NullTranslator);
        return b.Build();
    }

    /// <summary>Accepts "host:port,..." or a redis:// / rediss:// URL (Render Key Value gives a URL).</summary>
    public static ConfigurationOptions RedisConfiguration(IConfiguration config)
    {
        var raw = config["Redis:Configuration"] ?? "localhost:6379";
        ConfigurationOptions options;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "redis" or "rediss")
        {
            options = ConfigurationOptions.Parse($"{uri.Host}:{(uri.Port > 0 ? uri.Port : 6379)}");
            options.Ssl = uri.Scheme == "rediss";
            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo.Length == 2) { options.User = Uri.UnescapeDataString(userInfo[0]); options.Password = Uri.UnescapeDataString(userInfo[1]); }
            else if (userInfo[0].Length > 0) options.Password = Uri.UnescapeDataString(userInfo[0]);
        }
        else
        {
            options = ConfigurationOptions.Parse(raw);
        }
        options.AbortOnConnectFail = false; // start even if Redis is briefly down; readiness check reports it
        return options;
    }
}
