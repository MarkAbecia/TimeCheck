using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>
/// Real Postgres (with the real SQL migrations + seed) and real Redis in containers.
/// The API connects as the least-privilege seed role timecheck_api, so missing grants fail tests.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "http://127.0.0.1:55421/auth/v1";

    // Seed users (database/supabase/seed.sql): auth ids start with 3, app ids with 1.
    public static readonly Guid SuperAdminAuth = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid AdminAuth = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid LeaderAuth = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid MemberAuth = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static readonly Guid Leader2Auth = Guid.Parse("30000000-0000-0000-0000-000000000005");
    public static readonly Guid OtherChurchAuth = Guid.Parse("30000000-0000-0000-0000-000000000099");
    /// <summary>A fresh account: forced password change and privacy consent still owed.</summary>
    public static readonly Guid NewcomerAuth = Guid.Parse("30000000-0000-0000-0000-000000000077");

    public FakeAuthAdmin AuthAdmin { get; }
    public FakeStorage Storage { get; } = new();
    public FakeEmail Email { get; } = new();
    public FakePush Push { get; } = new();

    public ApiFactory() => AuthAdmin = new FakeAuthAdmin(() => _pg.GetConnectionString());
    public static readonly Guid LeaderAppId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid Group1 = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Group2 = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();
    private readonly RsaSecurityKey _signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public string AdminConnectionString => _pg.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_pg.StartAsync(), _redis.StartAsync());

        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await Exec(conn, SupabaseShim);
        var root = RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "database", "supabase", "migrations"), "*.sql").Order())
            await Exec(conn, await File.ReadAllTextAsync(file));
        await Exec(conn, await File.ReadAllTextAsync(Path.Combine(root, "database", "supabase", "seed.sql")));
        await Exec(conn, OtherChurchSql);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _pg.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // dev encryption keys match the seed ciphertext
        var appCs = new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Username = "timecheck_api", Password = "timecheck-dev-only" };
        builder.UseSetting("ConnectionStrings:Default", appCs.ConnectionString);
        builder.UseSetting("Redis:Configuration", _redis.GetConnectionString());
        builder.UseSetting("Auth:RequireAdminMfa", "true");

        builder.ConfigureServices(services => services.AddSingleton<TimeCheck.Application.Abstractions.IAuthAdmin>(AuthAdmin));
        builder.ConfigureServices(services => services.AddSingleton<TimeCheck.Application.Abstractions.IFileStorage>(Storage));
        builder.ConfigureServices(services => services.AddSingleton<TimeCheck.Application.Abstractions.IEmailSender>(Email));
        builder.ConfigureServices(services => services.AddSingleton<TimeCheck.Application.Abstractions.IPushSender>(Push));
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            var config = new OpenIdConnectConfiguration { Issuer = Issuer };
            config.SigningKeys.Add(_signingKey);
            o.Configuration = config;
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
        }));
    }

    public string Token(Guid authUserId, string aal = "aal1", string issuer = Issuer, DateTime? expires = null, SecurityKey? key = null, DateTime? issuedAt = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "authenticated",
            Subject = new ClaimsIdentity([new Claim("sub", authUserId.ToString()), new Claim("aal", aal), new Claim("role", "authenticated")]),
            NotBefore = (expires ?? DateTime.UtcNow.AddHours(1)).AddHours(-2),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            IssuedAt = issuedAt ?? DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(key ?? _signingKey, SecurityAlgorithms.RsaSha256),
        });
        return handler.WriteToken(token);
    }

    public HttpClient ClientFor(Guid authUserId, string aal = "aal1")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(authUserId, aal));
        return client;
    }

    public async Task<T?> Scalar<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? default : (T)result;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "supabase"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>The bits of a Supabase database our migrations and seed touch.</summary>
    private const string SupabaseShim = """
        create schema if not exists extensions;
        create extension if not exists pgcrypto with schema extensions;
        create schema if not exists auth;
        create schema if not exists storage;
        create role anon nologin; create role authenticated nologin; create role service_role nologin;
        create table auth.users (instance_id uuid, id uuid primary key, aud text, role text, email text, encrypted_password text,
          email_confirmed_at timestamptz, raw_app_meta_data jsonb, raw_user_meta_data jsonb, created_at timestamptz, updated_at timestamptz,
          confirmation_token text, email_change text, email_change_token_new text, recovery_token text);
        create table auth.identities (id uuid primary key, user_id uuid, provider_id text, identity_data jsonb, provider text,
          last_sign_in_at timestamptz, created_at timestamptz, updated_at timestamptz);
        create table auth.sessions (id uuid primary key default gen_random_uuid(), user_id uuid not null);
        create table storage.buckets (id text primary key, name text, public boolean, file_size_limit bigint, allowed_mime_types text[]);
        """;

    /// <summary>A second tenant, to prove church scoping.</summary>
    private const string OtherChurchSql = """
        do $$
        declare cid uuid := public.create_church('Other Church');
        begin
          insert into auth.users (id, email) values ('30000000-0000-0000-0000-000000000099', 'other@example.test');
          insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx, email,
                                       must_change_password, privacy_consent_version, privacy_consent_at)
          values ('10000000-0000-0000-0000-000000000099', cid, '30000000-0000-0000-0000-000000000099', 'ADMIN', 'Other', 'Admin', 'MALE', 'v1:unused', 'x', 'other@example.test',
                  false, '2026-11', now());

          insert into auth.users (id, email) values ('30000000-0000-0000-0000-000000000077', 'newcomer@timecheck.test');
          insert into public.app_users (id, church_id, auth_user_id, role, first_name, last_name, gender, birthdate_enc, name_birthdate_bidx, email)
          select '10000000-0000-0000-0000-000000000077', church_id, '30000000-0000-0000-0000-000000000077', 'MEMBER', 'New', 'Comer', 'FEMALE',
                 birthdate_enc, 'newcomer-bidx', 'newcomer@timecheck.test'
          from public.app_users where id = '10000000-0000-0000-0000-000000000004';
        end $$;
        """;
}

/// <summary>Stands in for the Supabase admin API; records calls, never sees a real key. Invites write auth.users like GoTrue does.</summary>
public sealed class FakeAuthAdmin(Func<string> connectionString) : TimeCheck.Application.Abstractions.IAuthAdmin
{
    public List<Guid> PasswordSetFor { get; } = [];
    public List<string> Invited { get; } = [];
    public List<Guid> Deleted { get; } = [];
    /// <summary>Factors per auth user that a reset would remove; tests set it.</summary>
    public Dictionary<Guid, int> Factors { get; } = [];

    public async Task<Guid> InviteUserAsync(string email, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString());
        await conn.OpenAsync(ct);
        await using (var check = new NpgsqlCommand("select count(*) from auth.users where email = @e", conn))
        {
            check.Parameters.AddWithValue("e", email);
            if ((long)(await check.ExecuteScalarAsync(ct))! > 0) throw new TimeCheck.Application.Common.ConflictException("An account with this email already exists.");
        }
        var id = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("insert into auth.users (id, email) values (@id, @e)", conn);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("e", email);
        await insert.ExecuteNonQueryAsync(ct);
        lock (Invited) Invited.Add(email);
        return id;
    }

    public async Task DeleteUserAsync(Guid authUserId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from auth.users where id = @id", conn);
        cmd.Parameters.AddWithValue("id", authUserId);
        await cmd.ExecuteNonQueryAsync(ct);
        lock (Deleted) Deleted.Add(authUserId);
    }

    public Task<int> RemoveMfaFactorsAsync(Guid authUserId, CancellationToken ct)
    {
        lock (Factors) return Task.FromResult(Factors.Remove(authUserId, out var n) ? n : 0);
    }

    public Task SetPasswordAsync(Guid authUserId, string newPassword, CancellationToken ct)
    {
        lock (PasswordSetFor) PasswordSetFor.Add(authUserId);
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>In-memory Supabase Storage: the tests check what was stored, never a real bucket.</summary>
public sealed class FakeStorage : TimeCheck.Application.Abstractions.IFileStorage
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Objects { get; } = new();

    public Task UploadAsync(string bucket, string path, byte[] content, string contentType, CancellationToken ct)
    {
        Objects[$"{bucket}/{path}"] = (content, contentType);
        return Task.CompletedTask;
    }

    public Task<byte[]> DownloadAsync(string bucket, string path, CancellationToken ct) => Task.FromResult(Objects[$"{bucket}/{path}"].Bytes);

    public Task<string> SignedUrlAsync(string bucket, string path, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult($"https://storage.test/sign/{bucket}/{path}?ttl={(int)ttl.TotalSeconds}");

    public Task DeleteAsync(string bucket, string path, CancellationToken ct)
    {
        Objects.TryRemove($"{bucket}/{path}", out _);
        return Task.CompletedTask;
    }
}

/// <summary>Records emails instead of sending them. Addresses containing "fail" throw, to exercise retries.</summary>
public sealed class FakeEmail : TimeCheck.Application.Abstractions.IEmailSender
{
    public System.Collections.Concurrent.ConcurrentBag<TimeCheck.Application.Abstractions.EmailMessage> Sent { get; } = [];
    public bool Enabled => true;
    public string AppUrl => "https://app.test";

    public Task SendAsync(TimeCheck.Application.Abstractions.EmailMessage message, CancellationToken ct)
    {
        if (message.To.Contains("fail", StringComparison.Ordinal)) throw new HttpRequestException("provider down");
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Records pushes. Endpoints containing "gone" answer as an expired subscription.</summary>
public sealed class FakePush : TimeCheck.Application.Abstractions.IPushSender
{
    public System.Collections.Concurrent.ConcurrentBag<(string Endpoint, string Payload)> Sent { get; } = [];
    public bool Enabled => true;
    public string? PublicKey => "BTestPublicKey";

    public Task<TimeCheck.Application.Abstractions.PushOutcome> SendAsync(string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct)
    {
        if (endpoint.Contains("gone", StringComparison.Ordinal)) return Task.FromResult(TimeCheck.Application.Abstractions.PushOutcome.Gone);
        Sent.Add((endpoint, payloadJson));
        return Task.FromResult(TimeCheck.Application.Abstractions.PushOutcome.Sent);
    }
}
