using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using TimeCheck.Infrastructure.Caching;
using TimeCheck.Infrastructure.Security;

namespace TimeCheck.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class AuthenticationTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoints_are_anonymous_and_ready_checks_db_and_redis()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task No_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-key")]
    [InlineData("unknown-user")]
    public async Task Bad_tokens_are_401(string kind)
    {
        var token = kind switch
        {
            "expired" => factory.Token(ApiFactory.MemberAuth, expires: DateTime.UtcNow.AddMinutes(-5)),
            "wrong-issuer" => factory.Token(ApiFactory.MemberAuth, issuer: "https://evil.example/auth/v1"),
            "wrong-key" => factory.Token(ApiFactory.MemberAuth, key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            _ => factory.Token(Guid.NewGuid()),
        };
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_role_and_group_from_the_database_with_decrypted_fields()
    {
        var me = await factory.ClientFor(ApiFactory.LeaderAuth).GetFromJsonAsync<JsonElement>("/api/v1/me");

        me.GetProperty("role").GetString().Should().Be("LEADER");
        me.GetProperty("mobile").GetString().Should().Be("09170000003");
        me.GetProperty("ledGroupIds").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(ApiFactory.Group1);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_correlation_id()
    {
        var response = await factory.ClientFor(ApiFactory.MemberAuth).GetAsync("/api/v1/me");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
    }

    [Fact]
    public void Only_health_and_docs_endpoints_allow_anonymous_access()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var anonymous = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? "").ToList();

        anonymous.Should().OnlyContain(p => p.StartsWith("/health/") || p.StartsWith("/openapi/") || p.StartsWith("/scalar"));
    }
}

[Collection(ApiTestGroup.Name)]
public class AuthorizationTests(ApiFactory factory)
{
    [Fact]
    public async Task Leader_sees_own_group()
    {
        var group = await factory.ClientFor(ApiFactory.LeaderAuth).GetFromJsonAsync<JsonElement>($"/api/v1/groups/{ApiFactory.Group1}");
        group.GetProperty("members").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("leader")]  // leads group 1, not group 2
    [InlineData("member")]  // member of group 1
    [InlineData("other-church-admin")]
    public async Task Cross_group_and_cross_church_access_is_404(string who)
    {
        var auth = who switch { "leader" => ApiFactory.LeaderAuth, "member" => ApiFactory.MemberAuth, _ => ApiFactory.OtherChurchAuth };
        var response = await factory.ClientFor(auth, "aal2").GetAsync($"/api/v1/groups/{ApiFactory.Group2}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_sees_any_group_in_their_church()
    {
        var response = await factory.ClientFor(ApiFactory.AdminAuth).GetAsync($"/api/v1/groups/{ApiFactory.Group2}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("member", "aal1", HttpStatusCode.Forbidden)]
    [InlineData("admin", "aal2", HttpStatusCode.Forbidden)]      // audit log is super admin only
    [InlineData("super", "aal1", HttpStatusCode.Forbidden)]      // MFA required
    [InlineData("super", "aal2", HttpStatusCode.OK)]
    public async Task Audit_log_requires_super_admin_with_mfa(string who, string aal, HttpStatusCode expected)
    {
        var auth = who switch { "member" => ApiFactory.MemberAuth, "admin" => ApiFactory.AdminAuth, _ => ApiFactory.SuperAdminAuth };
        var response = await factory.ClientFor(auth, aal).GetAsync("/api/v1/admin/audit-logs");
        response.StatusCode.Should().Be(expected);
    }
}

[Collection(ApiTestGroup.Name)]
public class ProfileTests(ApiFactory factory)
{
    [Fact]
    public async Task Updating_profile_encrypts_mobile_updates_blind_index_and_writes_audit()
    {
        var client = factory.ClientFor(ApiFactory.Leader2Auth);

        var response = await client.PatchAsJsonAsync("/api/v1/me", new { nickname = "Kuya Ben", mobile = "+63 917 999 0000", address = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString().Should().Be("Kuya Ben");

        var stored = await factory.Scalar<string>("select mobile_enc from app_users where email = 'leader2@timecheck.test'");
        stored.Should().StartWith("v1:").And.NotContain("917");
        var bidx = await factory.Scalar<string>("select mobile_bidx from app_users where email = 'leader2@timecheck.test'");
        var expected = factory.Services.GetRequiredService<TimeCheck.Application.Abstractions.IBlindIndexer>().Mobile("09179990000");
        bidx.Should().Be(expected, "+63 and 09 formats index the same");

        var audit = await factory.Scalar<string>("select after::text from audit_logs where action = 'profile.update' order by occurred_at desc limit 1");
        audit.Should().Contain("mobile").And.NotContain("917", "audit rows must not copy PII values");
    }

    [Fact]
    public async Task Unknown_fields_and_invalid_values_are_400_problem_details()
    {
        var client = factory.ClientFor(ApiFactory.MemberAuth);

        var unknown = await client.PatchAsJsonAsync("/api/v1/me", new { nickname = "x", role = "SUPER_ADMIN" });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalid = await client.PatchAsJsonAsync("/api/v1/me", new { nickname = "x", mobile = "call me" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalid.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("mobile", out _).Should().BeTrue();

        (await factory.Scalar<string>("select role::text from app_users where email = 'member@timecheck.test'")).Should().Be("MEMBER");
    }

    [Fact]
    public async Task Audit_log_is_keyset_paginated()
    {
        var member = factory.ClientFor(ApiFactory.MemberAuth);
        for (var i = 0; i < 3; i++) await member.PatchAsJsonAsync("/api/v1/me", new { nickname = $"n{i}" });

        var super = factory.ClientFor(ApiFactory.SuperAdminAuth, "aal2");
        var page1 = await super.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-logs?limit=2&action=profile.update");
        var cursor = page1.GetProperty("nextCursor").GetString();
        var page2 = await super.GetFromJsonAsync<JsonElement>($"/api/v1/admin/audit-logs?limit=2&action=profile.update&cursor={cursor}");

        page1.GetProperty("items").GetArrayLength().Should().Be(2);
        var ids1 = page1.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid());
        var ids2 = page2.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid());
        ids2.Should().NotBeEmpty().And.NotIntersectWith(ids1);
    }
}

[Collection(ApiTestGroup.Name)]
public class InfrastructureTests(ApiFactory factory)
{
    [Fact]
    public async Task Rate_limiter_blocks_after_limit_with_retry_after()
    {
        var limiter = new RedisRateLimiter(factory.Services.GetRequiredService<IConnectionMultiplexer>(), TimeProvider.System, NullLogger<RedisRateLimiter>.Instance);
        var key = $"test:{Guid.NewGuid()}";

        for (var i = 0; i < 3; i++) (await limiter.CheckAsync(key, 3, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        var blocked = await limiter.CheckAsync(key, 3, TimeSpan.FromMinutes(1));

        blocked.Allowed.Should().BeFalse();
        blocked.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Encryption_round_trips_detects_tampering_and_supports_rotation()
    {
        var k1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var k2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var bidx = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var old = new FieldEncryptor(Options.Create(new EncryptionOptions { ActiveKeyId = "1", Keys = { ["1"] = k1 }, BlindIndexKey = bidx }));
        var rotated = new FieldEncryptor(Options.Create(new EncryptionOptions { ActiveKeyId = "2", Keys = { ["1"] = k1, ["2"] = k2 }, BlindIndexKey = bidx }));

        var c1 = old.Encrypt("09171234567");
        rotated.Decrypt(c1).Should().Be("09171234567");
        rotated.Encrypt("x").Should().StartWith("v2:");
        old.Encrypt("same").Should().NotBe(old.Encrypt("same"), "nonces are random");

        var bytes = Convert.FromBase64String(c1[3..]);
        bytes[^1] ^= 0xFF;
        var tampered = () => old.Decrypt("v1:" + Convert.ToBase64String(bytes));
        tampered.Should().Throw<CryptographicException>();
    }
}
