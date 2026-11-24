using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TimeCheck.Application.Abstractions;
using TimeCheck.Infrastructure.Persistence;

namespace TimeCheck.Infrastructure.Caching;

/// <summary>Role and group come from app_users, cached in Redis for ≤ 60 s (§8.1). Redis failures fall back to the DB.</summary>
public sealed class UserDirectory(AppDbContext db, IConnectionMultiplexer redis, ILogger<UserDirectory> logger) : IUserDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static RedisKey Key(Guid authUserId) => $"tc:user:{authUserId}";

    public async Task<CurrentUserInfo?> FindByAuthUserIdAsync(Guid authUserId, CancellationToken ct)
    {
        try
        {
            var cached = await redis.GetDatabase().StringGetAsync(Key(authUserId));
            if (cached.HasValue) return JsonSerializer.Deserialize<CurrentUserInfo>(cached.ToString());
        }
        catch (RedisException e)
        {
            logger.LogWarning(e, "Redis unavailable; loading user from database");
        }

        // Tenant filter is off here: the church isn't known until the user is found.
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.AuthUserId == authUserId && u.DeletedAt == null)
            .Select(u => new { u.Id, u.ChurchId, u.Role, u.DeactivatedAt })
            .FirstOrDefaultAsync(ct);
        if (user is null) return null;

        var led = await db.Groups.IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.LeaderId == user.Id && g.ArchivedAt == null && g.ChurchId == user.ChurchId)
            .Select(g => g.Id).ToListAsync(ct);
        var groupId = await db.GroupMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == user.Id && m.LeftOn == null)
            .Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);

        var info = new CurrentUserInfo(user.Id, user.ChurchId, user.Role, user.DeactivatedAt is null, led, groupId);
        try
        {
            await redis.GetDatabase().StringSetAsync(Key(authUserId), JsonSerializer.Serialize(info), Ttl);
        }
        catch (RedisException e)
        {
            logger.LogWarning(e, "Redis unavailable; user not cached");
        }
        return info;
    }

    public async Task InvalidateAsync(Guid authUserId, CancellationToken ct) =>
        await redis.GetDatabase().KeyDeleteAsync(Key(authUserId));
}
