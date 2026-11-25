using System.Globalization;
using System.Text.Json;
using BlueID.Application.Auth;
using BlueID.Application.Common;
using BlueID.Domain;
using BlueID.Infrastructure.Audit;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueID.Application.Directory;

/// <summary>Who performs a directory change (for audit rows and event "actor").</summary>
public sealed record Actor(Guid? UserId, string? Name, string? Ip, string? UserAgent, string Type = "user")
{
    public static readonly Actor System = new(null, "system", null, null, "system");
}

public sealed record NewUser(
    string UserPrincipalName,
    string DisplayName,
    string? GivenName = null,
    string? FamilyName = null,
    string? JobTitle = null,
    string? Department = null,
    string? Locale = null,
    string? TimeZone = null,
    string? Password = null,
    bool MustChangePassword = false,
    UserSource Source = UserSource.Local,
    string? ExternalId = null,
    Guid? Id = null);

public sealed record UserPatch(
    string? DisplayName = null,
    string? GivenName = null,
    string? FamilyName = null,
    string? JobTitle = null,
    string? Department = null,
    string? Office = null,
    string? MobilePhone = null,
    string? BusinessPhone = null,
    string? Locale = null,
    string? TimeZone = null,
    string? HourCycle = null);

/// <summary>
/// The only path that writes the directory (PROMPT 11). Every change writes directory.change_log and events.outbox in
/// the same transaction as the change, plus the audit row; nothing is published outside the transaction.
/// </summary>
public interface IDirectoryWriter
{
    Task<Tenant> CreateTenantAsync(string name, string slug, TenantType type, string defaultLocale, string defaultTimeZone, Actor actor, CancellationToken ct);
    Task<TenantDomain> AddDomainAsync(Guid tenantId, string domain, bool verified, bool isDefault, Actor actor, CancellationToken ct);
    Task<User> CreateUserAsync(Guid tenantId, NewUser input, Actor actor, CancellationToken ct);
    Task<User> UpdateUserAsync(Guid tenantId, Guid userId, UserPatch patch, Actor actor, CancellationToken ct);
    Task SetUserEnabledAsync(Guid tenantId, Guid userId, bool enabled, string reason, Actor actor, CancellationToken ct);
    Task AssignAdminRoleAsync(Guid tenantId, Guid userId, string roleCode, Actor actor, CancellationToken ct);

    /// <summary>Revokes the given sessions of a user (null = all) and raises user.sessions_revoked (docs/05 7.2).</summary>
    Task<int> RevokeSessionsAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid>? sessionIds, string reason, Actor actor, CancellationToken ct);

    /// <summary>
    /// Sets a new password: every session ends and the security stamp changes (PROMPT 6.2); raises
    /// user.credentials_changed (kind = password). Returns the user with the new stamp.
    /// </summary>
    Task<User> ChangePasswordAsync(Guid tenantId, Guid userId, string newPassword, bool mustChange, string reason, Actor actor, CancellationToken ct);
}

public sealed class DirectoryWriter(BlueIdDbContext db, AuditWriter audit, PasswordService passwords, TimeProvider clock) : IDirectoryWriter
{
    public async Task<Tenant> CreateTenantAsync(string name, string slug, TenantType type, string defaultLocale, string defaultTimeZone, Actor actor, CancellationToken ct)
    {
        var tenant = new Tenant
        {
            Name = Identifiers.CleanText(name),
            Slug = slug.Trim().ToLowerInvariant(),
            Type = type,
            DefaultLocale = defaultLocale,
            DefaultTimeZone = TimeZones.Require(defaultTimeZone),
        };
        tenant.TenantId = tenant.Id;
        return await InTransactionAsync(async () =>
        {
            db.Tenants.Add(tenant);
            await SaveUniqueAsync(ct, ("ux_tenants_single_platform", "platform_tenant_exists"), ("ux_tenants_slug", "tenant_slug_taken"));
            await ChangeAsync(tenant.Id, "tenant", tenant.Id, "created", null, ct);
            await audit.WriteAsync(Audit(actor, tenant.Id, "tenant.created", "tenant", tenant.Id, null, new { tenant.Name, tenant.Slug, Type = tenant.Type.ToString() }), ct);
            return tenant;
        }, ct);
    }

    public async Task<TenantDomain> AddDomainAsync(Guid tenantId, string domain, bool verified, bool isDefault, Actor actor, CancellationToken ct)
    {
        if (!Identifiers.TryNormalizeDomain(domain, out var d))
        {
            throw new ValidationFailedException([new FieldError("domain", "domain_invalid")]);
        }

        var row = new TenantDomain
        {
            TenantId = tenantId,
            Domain = d,
            VerificationToken = "blueid-verify=" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            VerifiedAt = verified ? clock.GetUtcNow() : null,
            IsDefault = isDefault,
        };
        return await InTransactionAsync(async () =>
        {
            db.Domains.Add(row);
            await SaveUniqueAsync(ct, ("ux_domains_domain", "domain_taken"), ("ux_domains_one_default", "domain_default_exists"));
            if (verified)
            {
                await ChangeAsync(tenantId, "tenant", tenantId, "domain_verified", new { domain = d }, ct, "tenant.domain_verified");
            }

            await audit.WriteAsync(Audit(actor, tenantId, "tenant.domain_added", "domain", row.Id, null, new { domain = d, verified }), ct);
            return row;
        }, ct);
    }

    public async Task<User> CreateUserAsync(Guid tenantId, NewUser input, Actor actor, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (!Identifiers.TryNormalizeAddress(input.UserPrincipalName, out var upn))
        {
            errors.Add(new FieldError("userPrincipalName", "upn_invalid"));
        }

        var displayName = Identifiers.CleanText(input.DisplayName);
        if (displayName.Length is 0 or > 256)
        {
            errors.Add(new FieldError("displayName", "display_name_invalid"));
        }

        if (input.TimeZone is { } tz && !TimeZones.IsValid(tz))
        {
            errors.Add(new FieldError("timeZone", "time_zone_invalid"));
        }

        if (input.Password is { } pw && PasswordService.Validate(pw) is { } pwError)
        {
            errors.Add(new FieldError("password", pwError));
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        // The sign-in name must be in a verified domain of the tenant (PROMPT 6.3).
        var domain = Identifiers.DomainOf(upn);
        var domainOk = await db.Domains.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Domain == domain && x.VerifiedAt != null && x.DeletedAt == null, ct);
        if (!domainOk)
        {
            throw AppException.Unprocessable("domain_not_verified");
        }

        var user = new User
        {
            TenantId = tenantId,
            UserPrincipalName = upn,
            DisplayName = displayName,
            GivenName = Clean(input.GivenName),
            FamilyName = Clean(input.FamilyName),
            Mail = upn,
            JobTitle = Clean(input.JobTitle),
            Department = Clean(input.Department),
            Locale = input.Locale,
            TimeZone = input.TimeZone,
            Source = input.Source,
            ExternalId = input.ExternalId,
        };
        if (input.Id is { } id)
        {
            user.Id = id;
        }

        return await InTransactionAsync(async () =>
        {
            db.Users.Add(user);
            db.UserAddresses.Add(new UserAddress { TenantId = tenantId, UserId = user.Id, Address = upn, Type = AddressType.Primary });
            await SaveUniqueAsync(ct, ("ux_users_upn", "upn_taken"), ("ux_user_addresses_address", "address_taken"),
                ("ux_users_external_id", "external_id_taken"));
            if (input.Password is { } password)
            {
                await passwords.SetAsync(user, password, input.MustChangePassword, ct);
            }

            await ChangeAsync(tenantId, "user", user.Id, "created",
                new { upn, mail = upn, displayName, accountType = "member", source = EnumConversions.ToUpperName(user.Source).ToLowerInvariant() }, ct, "user.created");
            await audit.WriteAsync(Audit(actor, tenantId, "user.created", "user", user.Id, null, new { upn, displayName }), ct);
            return user;
        }, ct);
    }

    public async Task<User> UpdateUserAsync(Guid tenantId, Guid userId, UserPatch patch, Actor actor, CancellationToken ct)
    {
        var user = await FindUserAsync(tenantId, userId, ct);
        var before = Snapshot(user);
        var changed = new List<string>();
        void Set(string name, string? value, Func<string?> get, Action<string?> set)
        {
            if (value is null)
            {
                return;
            }

            var v = value.Length == 0 ? null : Identifiers.CleanText(value);
            if (!string.Equals(get(), v, StringComparison.Ordinal))
            {
                set(v);
                changed.Add(name);
            }
        }

        if (patch.DisplayName is { } dn && Identifiers.CleanText(dn).Length is 0 or > 256)
        {
            throw new ValidationFailedException([new FieldError("displayName", "display_name_invalid")]);
        }

        if (patch.TimeZone is { Length: > 0 } tz && !TimeZones.IsValid(tz))
        {
            throw new ValidationFailedException([new FieldError("timeZone", "time_zone_invalid")]);
        }

        if (patch.HourCycle is { Length: > 0 } hc && hc is not ("h12" or "h23"))
        {
            throw new ValidationFailedException([new FieldError("hourCycle", "hour_cycle_invalid")]);
        }

        if (patch.Locale is { Length: > 0 } loc && !Locales.IsValid(loc))
        {
            throw new ValidationFailedException([new FieldError("locale", "locale_invalid")]);
        }

        Set("displayName", patch.DisplayName, () => user.DisplayName, v => user.DisplayName = v!);
        Set("givenName", patch.GivenName, () => user.GivenName, v => user.GivenName = v);
        Set("familyName", patch.FamilyName, () => user.FamilyName, v => user.FamilyName = v);
        Set("jobTitle", patch.JobTitle, () => user.JobTitle, v => user.JobTitle = v);
        Set("department", patch.Department, () => user.Department, v => user.Department = v);
        Set("office", patch.Office, () => user.Office, v => user.Office = v);
        Set("mobilePhone", patch.MobilePhone, () => user.MobilePhone, v => user.MobilePhone = v);
        Set("businessPhone", patch.BusinessPhone, () => user.BusinessPhone, v => user.BusinessPhone = v);
        Set("locale", patch.Locale, () => user.Locale, v => user.Locale = v);
        Set("timeZone", patch.TimeZone, () => user.TimeZone, v => user.TimeZone = v);
        Set("hourCycle", patch.HourCycle, () => user.HourCycle, v => user.HourCycle = v);
        if (changed.Count == 0)
        {
            return user;
        }

        return await InTransactionAsync(async () =>
        {
            await db.SaveChangesAsync(ct);
            await ChangeAsync(tenantId, "user", user.Id, "updated", new { changed }, ct, "user.updated");
            await audit.WriteAsync(Audit(actor, tenantId, "user.updated", "user", user.Id, before, Snapshot(user)), ct);
            return user;
        }, ct);
    }

    public async Task SetUserEnabledAsync(Guid tenantId, Guid userId, bool enabled, string reason, Actor actor, CancellationToken ct)
    {
        var user = await FindUserAsync(tenantId, userId, ct);
        var target = enabled ? UserStatus.Active : UserStatus.Disabled;
        if (user.Status == target)
        {
            return;
        }

        await InTransactionAsync(async () =>
        {
            user.Status = target;
            if (!enabled)
            {
                // A disabled account loses every session at once; products are told by the event (docs/05 7.5).
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                await db.Sessions.IgnoreQueryFilters().Where(s => s.UserId == userId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()).SetProperty(x => x.RevokeReason, "user_disabled"), ct);
            }

            await db.SaveChangesAsync(ct);
            await ChangeAsync(tenantId, "user", userId, enabled ? "enabled" : "disabled",
                enabled ? new { } : new { reason }, ct, enabled ? "user.enabled" : "user.disabled");
            await audit.WriteAsync(Audit(actor, tenantId, enabled ? "user.enabled" : "user.disabled", "user", userId, null, new { reason }), ct);
            return true;
        }, ct);
    }

    public async Task AssignAdminRoleAsync(Guid tenantId, Guid userId, string roleCode, Actor actor, CancellationToken ct)
    {
        await FindUserAsync(tenantId, userId, ct);
        await InTransactionAsync(async () =>
        {
            db.AdminRoleAssignments.Add(new AdminRoleAssignment { TenantId = tenantId, UserId = userId, RoleCode = roleCode });
            await SaveUniqueAsync(ct, ("ux_admin_role_assignments", "role_already_assigned"));
            await audit.WriteAsync(Audit(actor, tenantId, "admin_role.assigned", "user", userId, null, new { role = roleCode }), ct);
            return true;
        }, ct);
    }

    public async Task<int> RevokeSessionsAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid>? sessionIds, string reason, Actor actor, CancellationToken ct)
    {
        var user = await FindUserAsync(tenantId, userId, ct);
        return await InTransactionAsync(async () =>
        {
            var q = db.Sessions.IgnoreQueryFilters().Where(x => x.UserId == userId && x.TenantId == tenantId && x.RevokedAt == null);
            if (sessionIds is not null)
            {
                q = q.Where(x => sessionIds.Contains(x.Id));
            }

            var ids = await q.Select(x => x.Id).ToListAsync(ct);
            if (ids.Count == 0)
            {
                return 0;
            }

            await db.Sessions.IgnoreQueryFilters().Where(x => ids.Contains(x.Id))
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.RevokedAt, clock.GetUtcNow()).SetProperty(v => v.RevokeReason, reason), ct);
            if (sessionIds is null)
            {
                // All sessions: the stamp changes so a cookie issued a moment ago cannot slip through.
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                await db.SaveChangesAsync(ct);
            }

            await ChangeAsync(tenantId, "user", userId, "sessions_revoked",
                new { reason, sessionIds = sessionIds is null ? null : ids }, ct, "user.sessions_revoked");
            await audit.WriteAsync(Audit(actor, tenantId, "user.sessions_revoked", "user", userId, null, new { reason, count = ids.Count, all = sessionIds is null }), ct);
            return ids.Count;
        }, ct);
    }

    public async Task<User> ChangePasswordAsync(Guid tenantId, Guid userId, string newPassword, bool mustChange, string reason, Actor actor, CancellationToken ct)
    {
        if (PasswordService.Validate(newPassword) is { } error)
        {
            throw new ValidationFailedException([new FieldError("password", error)]);
        }

        var user = await FindUserAsync(tenantId, userId, ct);
        var current = await passwords.CurrentAsync(userId, ct);
        if (current is not null && await passwords.VerifyAsync(user, current, newPassword, ct))
        {
            throw new ValidationFailedException([new FieldError("password", "password_same_as_current")]);
        }

        return await InTransactionAsync(async () =>
        {
            await passwords.SetAsync(user, newPassword, mustChange, ct);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            await db.Sessions.IgnoreQueryFilters().Where(x => x.UserId == userId && x.RevokedAt == null)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.RevokedAt, clock.GetUtcNow()).SetProperty(v => v.RevokeReason, "password_changed"), ct);
            await ChangeAsync(tenantId, "user", userId, "credentials_changed", new { kind = "password" }, ct, "user.credentials_changed");
            await audit.WriteAsync(Audit(actor, tenantId, "user.password_changed", "user", userId, null, new { reason, mustChange }), ct);
            return user;
        }, ct);
    }

    private async Task<User> FindUserAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId && u.DeletedAt == null, ct)
        ?? throw AppException.NotFound("user_not_found");

    /// <summary>change_log row (per-tenant sequence) and, when given, the outbox event (per-subject sequence).</summary>
    private async Task ChangeAsync(Guid tenantId, string objectType, Guid objectId, string change, object? payload, CancellationToken ct, string? eventType = null)
    {
        var now = clock.GetUtcNow();
        var seq = await db.Database.SqlQuery<long>($"""
            INSERT INTO directory.tenant_sequences (tenant_id, last_seq) VALUES ({tenantId}, 1)
            ON CONFLICT (tenant_id) DO UPDATE SET last_seq = directory.tenant_sequences.last_seq + 1
            RETURNING last_seq AS value
            """).ToListAsync(ct);
        db.ChangeLog.Add(new ChangeLogEntry { TenantId = tenantId, ObjectType = objectType, ObjectId = objectId, Change = change, ChangeSeq = seq[0], OccurredAt = now });
        if (eventType is not null)
        {
            var subjectSeq = await db.Database.SqlQuery<long>($"""
                INSERT INTO events.subject_sequences (subject_type, subject_id, last_seq) VALUES ({objectType}, {objectId}, 1)
                ON CONFLICT (subject_type, subject_id) DO UPDATE SET last_seq = events.subject_sequences.last_seq + 1
                RETURNING last_seq AS value
                """).ToListAsync(ct);
            db.Outbox.Add(new OutboxEvent
            {
                TenantId = tenantId,
                SubjectType = objectType,
                SubjectId = objectId,
                EventType = eventType,
                Payload = JsonSerializer.Serialize(payload ?? new { }),
                SubjectSeq = subjectSeq[0],
                Txn = CurrentTxn,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private Guid CurrentTxn { get; set; }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (CurrentTxn == Guid.Empty || db.Database.CurrentTransaction is null)
        {
            // One txn id per database transaction: events of one operation share it (docs/05 7.1).
            CurrentTxn = Guid.CreateVersion7();
        }

        if (db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>Maps unique-index violations to stable conflict codes (the database is the judge, lesson 1).</summary>
    private async Task SaveUniqueAsync(CancellationToken ct, params (string Index, string Code)[] map)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            foreach (var (index, code) in map)
            {
                if (string.Equals(pg.ConstraintName, index, StringComparison.Ordinal))
                {
                    throw AppException.Conflict(code);
                }
            }

            throw AppException.Conflict("conflict");
        }
    }

    private static string? Clean(string? v) => v is null ? null : Identifiers.CleanText(v) is { Length: > 0 } s ? s : null;

    private static object Snapshot(User u) => new
    {
        u.DisplayName, u.GivenName, u.FamilyName, u.JobTitle, u.Department, u.Office, u.MobilePhone, u.BusinessPhone, u.Locale, u.TimeZone, u.HourCycle,
    };

    private static AuditEntry Audit(Actor a, Guid tenantId, string action, string entity, Guid id, object? oldValue, object? newValue) =>
        new(action, AuditResult.Success, tenantId, a.UserId, a.Name, a.Ip, a.UserAgent, entity, id.ToString("D", CultureInfo.InvariantCulture), oldValue, newValue);
}

public static class TimeZones
{
    public static bool IsValid(string id) => id == "UTC" || (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) && tz.HasIanaId);

    public static string Require(string id) => IsValid(id) ? id : throw new ValidationFailedException([new FieldError("timeZone", "time_zone_invalid")]);
}

public static class Locales
{
    public static bool IsValid(string tag)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            return !string.IsNullOrEmpty(c.Name);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
