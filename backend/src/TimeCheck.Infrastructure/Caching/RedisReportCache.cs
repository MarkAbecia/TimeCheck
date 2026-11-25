using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TimeCheck.Application.Abstractions;

namespace TimeCheck.Infrastructure.Caching;

/// <summary>
/// Cache-aside for report results. Keys carry the church's version number; a write bumps it, so every older entry is
/// simply never read again and expires on its own.
/// </summary>
public sealed class RedisReportCache(IConnectionMultiplexer redis, ILogger<RedisReportCache> logger) : IReportCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    // shortcut: builds are shared within one API instance only; each instance may build once. Fine for a few instances.
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _building = new();

    private static string VersionKey(Guid churchId) => $"reports:ver:{churchId}";

    public async Task<byte[]> GetOrCreateAsync(Guid churchId, string key, Func<CancellationToken, Task<byte[]>> build, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        string full;
        try
        {
            var version = (long?)await db.StringGetAsync(VersionKey(churchId)) ?? 0;
            full = $"reports:{churchId}:{version}:{key}";
            var hit = await db.StringGetAsync(full);
            if (hit.HasValue) return (byte[])hit!;
        }
        catch (RedisException e)
        {
            logger.LogWarning(e, "Report cache unavailable; building without it");
            return await build(ct);
        }

        var lazy = _building.GetOrAdd(full, _ => new Lazy<Task<byte[]>>(() => build(ct)));
        try
        {
            var bytes = await lazy.Value;
            try { await db.StringSetAsync(full, bytes, Ttl); }
            catch (RedisException e) { logger.LogWarning(e, "Couldn't cache a report"); }
            return bytes;
        }
        finally
        {
            _building.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(full, lazy));
        }
    }

    public async Task InvalidateAsync(Guid churchId)
    {
        try { await redis.GetDatabase().StringIncrementAsync(VersionKey(churchId)); }
        catch (RedisException e) { logger.LogWarning(e, "Couldn't invalidate cached reports for {ChurchId}", churchId); }
    }
}
