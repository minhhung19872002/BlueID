using BlueID.Domain;
using BlueID.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlueID.Infrastructure.Persistence;

public sealed class BlueIdDbContext(DbContextOptions<BlueIdDbContext> options, ITenantContext tenant)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Read by the global query filters (EF evaluates it per query).</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> Domains => Set<TenantDomain>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<SubjectSequence> SubjectSequences => Set<SubjectSequence>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<PasswordRecord> Passwords => Set<PasswordRecord>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SignInLog> SignInLogs => Set<SignInLog>();
    public DbSet<Lockout> Lockouts => Set<Lockout>();
    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();
    public DbSet<AdminRoleAssignment> AdminRoleAssignments => Set<AdminRoleAssignment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<ApiResource> ApiResources => Set<ApiResource>();
    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<SessionClient> SessionClients => Set<SessionClient>();
    public DbSet<LogoutDelivery> LogoutDeliveries => Set<LogoutDelivery>();
    public DbSet<UsedToken> UsedTokens => Set<UsedToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("citext");

        Owned<Tenant>(b, "tenant", "tenants", e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(63);
            e.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_tenants_slug");
            e.Property(x => x.Status).UpperEnum().HasMaxLength(16);
            e.Property(x => x.Type).UpperEnum().HasMaxLength(16);
            // "Only one platform tenant" is a database rule.
            e.HasIndex(x => x.Type).IsUnique().HasFilter("type = 'PLATFORM'").HasDatabaseName("ux_tenants_single_platform");
            e.Property(x => x.Branding).HasColumnType("jsonb");
            e.Property(x => x.Settings).HasColumnType("jsonb");
        });
        Owned<TenantDomain>(b, "tenant", "domains", e =>
        {
            e.Property(x => x.Domain).HasMaxLength(253);
            e.HasIndex(x => x.Domain).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_domains_domain");
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_default AND deleted_at IS NULL").HasDatabaseName("ux_domains_one_default");
        });

        Owned<User>(b, "directory", "users", e =>
        {
            e.Property(x => x.UserPrincipalName).HasColumnType("citext").HasMaxLength(320);
            e.HasIndex(x => x.UserPrincipalName).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_users_upn");
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Mail).HasColumnType("citext").HasMaxLength(320);
            e.Property(x => x.AccountType).UpperEnum().HasMaxLength(16);
            e.Property(x => x.Source).UpperEnum().HasMaxLength(16);
            e.Property(x => x.Status).UpperEnum().HasMaxLength(16);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.DisplayName, x.Id });
            e.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL AND deleted_at IS NULL").HasDatabaseName("ux_users_external_id");
        });
        Owned<UserAddress>(b, "directory", "user_addresses", e =>
        {
            e.Property(x => x.Address).HasColumnType("citext").HasMaxLength(320);
            e.HasIndex(x => x.Address).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_user_addresses_address");
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("type = 'PRIMARY' AND deleted_at IS NULL").HasDatabaseName("ux_user_addresses_one_primary");
            e.Property(x => x.Type).UpperEnum().HasMaxLength(16);
        });
        b.Entity<ChangeLogEntry>(e =>
        {
            e.ToTable("change_log", "directory");
            e.HasIndex(x => new { x.TenantId, x.ChangeSeq }).IsUnique();
            e.Property(x => x.ObjectType).HasMaxLength(32);
            e.Property(x => x.Change).HasMaxLength(32);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        b.Entity<TenantSequence>(e =>
        {
            e.ToTable("tenant_sequences", "directory");
            e.HasKey(x => x.TenantId);
        });

        b.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox", "events");
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.SubjectType).HasMaxLength(16);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.HasIndex(x => new { x.SubjectType, x.SubjectId, x.SubjectSeq }).IsUnique();
        });
        b.Entity<SubjectSequence>(e =>
        {
            e.ToTable("subject_sequences", "events");
            e.HasKey(x => new { x.SubjectType, x.SubjectId });
            e.Property(x => x.SubjectType).HasMaxLength(16);
        });

        Owned<PasswordRecord>(b, "auth", "passwords", e =>
        {
            e.Property(x => x.Hash).HasMaxLength(512);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
        Owned<Session>(b, "auth", "sessions", e =>
        {
            e.Property(x => x.Amr).HasColumnType("jsonb");
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.RevokeReason).HasMaxLength(64);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
        b.Entity<SignInLog>(e =>
        {
            e.ToTable("sign_in_logs", "auth");
            e.Property(x => x.Result).UpperEnum().HasMaxLength(16);
            e.Property(x => x.ConditionalAccess).HasColumnType("jsonb");
            e.Property(x => x.Risk).HasColumnType("jsonb");
            e.Property(x => x.FailureCode).HasMaxLength(64);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasIndex(x => new { x.UserId, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        b.Entity<Lockout>(e =>
        {
            e.ToTable("lockouts", "auth");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(80);
        });

        b.Entity<AdminRole>(e =>
        {
            e.ToTable("admin_roles", "policy");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Permissions).HasColumnType("jsonb");
        });
        Owned<AdminRoleAssignment>(b, "policy", "admin_role_assignments", e =>
        {
            e.Property(x => x.RoleCode).HasMaxLength(64);
            e.Property(x => x.Scope).UpperEnum().HasMaxLength(16);
            e.HasIndex(x => new { x.RoleCode, x.UserId, x.GroupId, x.Scope, x.ScopeId }).IsUnique()
                .HasFilter("deleted_at IS NULL").AreNullsDistinct(false).HasDatabaseName("ux_admin_role_assignments");
            e.HasOne<AdminRole>().WithMany().HasForeignKey(x => x.RoleCode).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs", "audit");
            e.Property(x => x.Action).HasMaxLength(96);
            e.Property(x => x.Result).UpperEnum().HasMaxLength(16);
            e.Property(x => x.OldValue).HasColumnType("jsonb");
            e.Property(x => x.NewValue).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys", "auth");

        b.Entity<SigningKey>(e =>
        {
            e.ToTable("signing_keys", "keys");
            e.Property(x => x.Kid).HasMaxLength(64);
            e.HasIndex(x => x.Kid).IsUnique().HasDatabaseName("ux_signing_keys_kid");
            e.Property(x => x.Algorithm).HasMaxLength(16);
            e.Property(x => x.PublicJwk).HasColumnType("jsonb");
            e.Property(x => x.Status).UpperEnum().HasMaxLength(16);
            // One ACTIVE key and at most one NEXT key per algorithm: database rules.
            e.HasIndex(x => x.Algorithm).IsUnique().HasFilter("status = 'ACTIVE'").HasDatabaseName("ux_signing_keys_one_active");
            e.HasIndex(x => new { x.Algorithm, x.Status }).IsUnique().HasFilter("status = 'NEXT'").HasDatabaseName("ux_signing_keys_one_next");
        });

        Owned<ApiResource>(b, "apps", "resources", e =>
        {
            e.Property(x => x.Identifier).HasMaxLength(512);
            e.HasIndex(x => x.Identifier).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_resources_identifier");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Scopes).HasColumnType("jsonb");
            e.Property(x => x.AppRoles).HasColumnType("jsonb");
            e.Property(x => x.LicenseSkus).HasColumnType("jsonb");
            e.Property(x => x.EventAuth).HasColumnType("jsonb");
            e.Property(x => x.TokenLifetimeOverrides).HasColumnType("jsonb");
            e.Property(x => x.GroupClaims).UpperEnum().HasMaxLength(16);
        });
        Owned<ClientApplication>(b, "apps", "applications", e =>
        {
            e.Property(x => x.ClientId).HasMaxLength(200);
            e.HasIndex(x => x.ClientId).IsUnique().HasFilter("deleted_at IS NULL").HasDatabaseName("ux_applications_client_id");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).UpperEnum().HasMaxLength(16);
            e.Property(x => x.BackchannelLogoutUri).HasMaxLength(2048);
        });
        Owned<Consent>(b, "apps", "consents", e =>
        {
            e.Property(x => x.Scopes).HasColumnType("jsonb");
            // One live consent per (tenant, application, user); null user = tenant-wide administrator consent.
            e.HasIndex(x => new { x.TenantId, x.ApplicationId, x.UserId }).IsUnique()
                .HasFilter("revoked_at IS NULL AND deleted_at IS NULL").AreNullsDistinct(false).HasDatabaseName("ux_consents_live");
        });
        b.Entity<SessionClient>(e =>
        {
            e.ToTable("session_clients", "auth");
            e.HasKey(x => new { x.SessionId, x.ClientId });
            e.Property(x => x.ClientId).HasMaxLength(200);
        });
        b.Entity<LogoutDelivery>(e =>
        {
            e.ToTable("logout_deliveries", "auth");
            e.Property(x => x.Status).UpperEnum().HasMaxLength(16);
            e.Property(x => x.ClientId).HasMaxLength(200);
            e.Property(x => x.Uri).HasMaxLength(2048);
            e.HasIndex(x => new { x.SessionId, x.ClientId }).IsUnique().HasDatabaseName("ux_logout_deliveries_session_client");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });

        b.Entity<UsedToken>(e =>
        {
            e.ToTable("used_tokens", "auth");
            e.HasKey(x => new { x.Kind, x.Value });
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Value).HasMaxLength(512);
            e.HasIndex(x => x.ExpiresAt);
        });

        // OpenIddict's own tables (applications, authorizations, scopes, tokens) in their schema (PROMPT 4 oauth.*).
        b.UseOpenIddict<Guid>();
        b.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("applications", "oauth");
        b.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("authorizations", "oauth");
        b.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("scopes", "oauth");
        b.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("tokens", "oauth");
    }

    /// <summary>A tenant-owned table: schema, closed-by-default tenant filter plus soft-delete filter.</summary>
    private void Owned<T>(ModelBuilder b, string schema, string table, Action<EntityTypeBuilder<T>> configure) where T : Entity
    {
        b.Entity<T>(e =>
        {
            e.ToTable(table, schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId && x.DeletedAt == null);
            configure(e);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        var now = DateTimeOffset.UtcNow;
        var actor = tenant.UserId;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    entry.Entity.UpdatedAt = entry.Entity.CreatedAt;
                    entry.Entity.CreatedBy ??= actor;
                    entry.Entity.UpdatedBy ??= actor;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor ?? entry.Entity.UpdatedBy;
                    break;
            }
        }
    }
}
