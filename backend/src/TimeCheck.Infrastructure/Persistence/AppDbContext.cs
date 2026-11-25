using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Common;
using TimeCheck.Domain.Entities;
using TimeCheck.Infrastructure.Security;

namespace TimeCheck.Infrastructure.Persistence;

/// <summary>Tenant scope for global query filters. API: the current user's church. Worker: all churches.</summary>
public interface ITenantScope
{
    Guid? ChurchId { get; }
}

/// <summary>
/// Maps onto the schema in database/supabase/migrations. Never generate EF migrations (ADR 0001).
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options, FieldEncryptor encryptor, IBlindIndexer blindIndexer, ITenantScope tenant)
    : DbContext(options), IAppDbContext
{
    private const string MobileBidx = BlindIndexColumns.Mobile, NameBirthdateBidx = BlindIndexColumns.NameBirthdate;

    // Read per query: EF parameterizes context members used in query filters.
    private Guid? TenantChurchId => tenant.ChurchId;

    public DbSet<Church> Churches => Set<Church>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CellGroup> Groups => Set<CellGroup>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<EventType> EventTypes => Set<EventType>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventAudience> EventAudiences => Set<EventAudience>();
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<Devotion> Devotions => Set<Devotion>();
    public void DiscardChanges() => ChangeTracker.Clear();

    public Task<int> PurgeAuditLogsAsync(Guid churchId, DateTimeOffset before, CancellationToken ct) =>
        Database.SqlQuery<int>($"select public.purge_audit_logs({churchId}, {before}) as \"Value\"").SingleAsync(ct);
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<RoleChange> RoleChanges => Set<RoleChange>();
    public DbSet<MemberStatusChange> MemberStatusChanges => Set<MemberStatusChange>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ReportJob> ReportJobs => Set<ReportJob>();
    public DbSet<AttendanceFact> AttendanceFacts => Set<AttendanceFact>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Blind indexes are derived from plaintext, so they're recomputed whenever a user row is written.
        foreach (var entry in ChangeTracker.Entries<AppUser>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var u = entry.Entity;
            entry.Property<string?>(MobileBidx).CurrentValue = u.Mobile is null ? null : blindIndexer.Mobile(u.Mobile);
            entry.Property<string>(NameBirthdateBidx).CurrentValue = blindIndexer.NameBirthdate(u.FirstName, u.LastName, u.Birthdate);
        }
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        var encString = new ValueConverter<string?, string?>(v => encryptor.Encrypt(v!), v => encryptor.Decrypt(v!));
        var encDate = new ValueConverter<DateOnly, string>(
            v => encryptor.Encrypt(v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            v => DateOnly.ParseExact(encryptor.Decrypt(v), "yyyy-MM-dd", CultureInfo.InvariantCulture));

        b.Entity<Church>(e =>
        {
            e.ToTable("churches");
            e.Property(x => x.Settings).HasColumnType("jsonb");
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_users");
            e.Property(x => x.Birthdate).HasColumnName("birthdate_enc").HasConversion(encDate);
            e.Property(x => x.Mobile).HasColumnName("mobile_enc").HasConversion(encString);
            e.Property(x => x.Address).HasColumnName("address_enc").HasConversion(encString);
            e.Property(x => x.GuardianContact).HasColumnName("guardian_contact_enc").HasConversion(encString);
            e.Property<string?>(MobileBidx).HasColumnName("mobile_bidx");
            e.Property<string>(NameBirthdateBidx).HasColumnName("name_birthdate_bidx");
            e.Property(x => x.Email).HasColumnType("citext");
            e.Property(x => x.Settings).HasColumnType("jsonb");
            e.Ignore(x => x.DisplayName);
            e.Ignore(x => x.IsActive);
            e.HasQueryFilter(x => x.DeletedAt == null && (TenantChurchId == null || x.ChurchId == TenantChurchId));
        });

        b.Entity<CellGroup>(e => { e.ToTable("cell_groups"); Tenant(e); });
        b.Entity<GroupMembership>(e => { e.ToTable("group_memberships"); Tenant(e); });
        b.Entity<EventType>(e => { e.ToTable("event_types"); Tenant(e); });
        b.Entity<Event>(e => { e.ToTable("events"); Tenant(e); });
        b.Entity<EventAudience>(e => e.ToTable("event_audiences"));
        b.Entity<Attendance>(e =>
        {
            e.ToTable("attendance");
            e.Property(x => x.RowVersion).IsRowVersion(); // xmin
            Tenant(e);
        });
        b.Entity<Devotion>(e =>
        {
            e.ToTable("devotions");
            e.Property(x => x.AiTextScore).HasPrecision(5, 2);
            e.Property(x => x.AiImageScore).HasPrecision(5, 2);
            // The AI job saves a score only if the content it scored is still the content stored.
            e.Property(x => x.ContentHash).IsConcurrencyToken();
            Tenant(e);
        });
        b.Entity<Invite>(e =>
        {
            e.ToTable("invites");
            e.Property(x => x.GuestContact).HasColumnName("guest_contact_enc").HasConversion(encString);
            e.HasQueryFilter(x => x.DeletedAt == null && (TenantChurchId == null || x.ChurchId == TenantChurchId));
        });
        b.Entity<RoleChange>(e => { e.ToTable("role_changes"); Tenant(e); });
        b.Entity<MemberStatusChange>(e => { e.ToTable("member_status_changes"); Tenant(e); });
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Channels).HasColumnType("text[]");
            Tenant(e);
        });
        b.Entity<PushSubscription>(e => { e.ToTable("push_subscriptions"); Tenant(e); });
        b.Entity<ReportJob>(e =>
        {
            e.ToTable("report_jobs");
            e.Property(x => x.Params).HasColumnType("jsonb");
            Tenant(e);
        });
        b.Entity<AttendanceFact>(e => { e.ToView("attendance_facts"); e.HasNoKey(); Tenant(e); });
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            Tenant(e);
        });

        // Ids are generated client-side; timestamps come from DB defaults/triggers.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.FindProperty("Id")?.SetValueGenerationStrategy(null);
            if (entity.FindProperty(nameof(BaseEntity.Id)) is { } id) id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            if (entity.FindProperty(nameof(BaseEntity.CreatedAt)) is { } created)
            {
                created.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
                created.SetDefaultValueSql("now()");
            }
            if (entity.FindProperty(nameof(BaseEntity.UpdatedAt)) is { } updated)
            {
                updated.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                updated.SetDefaultValueSql("now()");
            }
        }
    }

    private void Tenant<T>(EntityTypeBuilder<T> e) where T : class, IChurchScoped =>
        e.HasQueryFilter(x => TenantChurchId == null || x.ChurchId == TenantChurchId);
}
