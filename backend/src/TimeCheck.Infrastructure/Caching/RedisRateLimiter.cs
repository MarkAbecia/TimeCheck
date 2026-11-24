using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TimeCheck.Infrastructure.Caching;

public sealed record RateLimitDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>Sliding-window log in a Redis sorted set, shared by all API instances (§8.3, §9).</summary>
public sealed class RedisRateLimiter(IConnectionMultiplexer redis, TimeProvider clock, ILogger<RedisRateLimiter> logger)
{
    // KEYS[1]=key ARGV: now(ms), window(ms), limit, member
    private const string Script = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, ARGV[1] - ARGV[2])
        local count = redis.call('ZCARD', KEYS[1])
        if count < tonumber(ARGV[3]) then
          redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
          return 0
        end
        local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        return (oldest[2] + ARGV[2]) - ARGV[1]
        """;

    public async Task<RateLimitDecision> CheckAsync(string key, int limit, TimeSpan window)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        try
        {
            var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(Script,
                [(RedisKey)$"tc:rl:{key}"],
                [now, (long)window.TotalMilliseconds, limit, $"{now}:{Guid.NewGuid():N}"]);
            return result <= 0 ? new(true, TimeSpan.Zero) : new(false, TimeSpan.FromMilliseconds(Math.Max(result, 1000)));
        }
        catch (RedisException e)
        {
            // shortcut: fails open when Redis is down (availability over strictness); Supabase Auth keeps its own login limits.
            logger.LogWarning(e, "Rate limiter unavailable; allowing request");
            return new(true, TimeSpan.Zero);
        }
    }
}
