using Microsoft.EntityFrameworkCore;
using TimeCheck.Domain.Entities;
using TimeCheck.Domain.Enums;

namespace TimeCheck.Application.Abstractions;

/// <summary>The signed-in user, resolved from app_users (never from token claims). See ADR 0002.</summary>
public sealed record CurrentUserInfo(
    Guid AppUserId, Guid ChurchId, UserRole Role, bool IsActive, IReadOnlyList<Guid> LedGroupIds, Guid? GroupId);

public interface ICurrentUser
{
    /// <summary>Throws if there is no authenticated user (should never happen behind the fallback policy).</summary>
    CurrentUserInfo Info { get; }
}

/// <summary>Looks up (and caches) the app user behind a Supabase auth user id.</summary>
public interface IUserDirectory
{
    Task<CurrentUserInfo?> FindByAuthUserIdAsync(Guid authUserId, CancellationToken ct);
    Task InvalidateAsync(Guid authUserId, CancellationToken ct);
}

/// <summary>Request metadata for audit rows.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

/// <summary>HMAC blind index for searching/deduplicating encrypted columns (§8.4).</summary>
public interface IBlindIndexer
{
    string Mobile(string mobile);
    string NameBirthdate(string firstName, string lastName, DateOnly birthdate);
    string Ip(string ip);
}

public interface IAppDbContext
{
    DbSet<Church> Churches { get; }
    DbSet<AppUser> Users { get; }
    DbSet<CellGroup> Groups { get; }
    DbSet<GroupMembership> GroupMemberships { get; }
    DbSet<EventType> EventTypes { get; }
    DbSet<Event> Events { get; }
    DbSet<EventAudience> EventAudiences { get; }
    DbSet<Attendance> Attendance { get; }
    DbSet<Devotion> Devotions { get; }
    DbSet<Invite> Invites { get; }
    DbSet<RoleChange> RoleChanges { get; }
    DbSet<MemberStatusChange> MemberStatusChanges { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ReportJob> ReportJobs { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
