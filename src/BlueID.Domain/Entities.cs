namespace BlueID.Domain;

/// <summary>
/// Columns every business table has (PROMPT 4): id, tenant_id, created/updated stamps and the soft-delete
/// marker. Unique indexes are partial (<c>WHERE deleted_at IS NULL</c>).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum TenantStatus { Active, Suspended, Trial, Deleted }

public enum TenantType { Platform, Customer }

/// <summary>A customer organisation, or the single platform tenant. Its own tenant_id is its id.</summary>
public sealed class Tenant : Entity
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public TenantType Type { get; set; } = TenantType.Customer;
    public string DefaultLocale { get; set; } = "en";
    public string DefaultTimeZone { get; set; } = "UTC";
    public string DataRegion { get; set; } = "default";
    /// <summary>jsonb: logo, colours, background, sign-in text, terms/privacy links.</summary>
    public string Branding { get; set; } = "{}";
    /// <summary>jsonb: tenant settings (token lifetimes, toggles). Every key has a reader (lesson 4).</summary>
    public string Settings { get; set; } = "{}";
}

/// <summary>A domain of a tenant. Unique across the whole system (one domain, one tenant).</summary>
public sealed class TenantDomain : Entity
{
    /// <summary>A-label (Punycode), lower case.</summary>
    public string Domain { get; set; } = "";
    public string VerificationToken { get; set; } = "";
    public DateTimeOffset? VerifiedAt { get; set; }
    public bool IsDefault { get; set; }
    public Guid? FederationId { get; set; }
}

public enum AccountType { Member, Guest }

public enum UserSource { Local, Scim, LdapSync, Jit }

public enum UserStatus { Active, Disabled, Deleted }

/// <summary>
/// A person in the directory. <see cref="Entity.Id"/> is the <c>sub</c> claim: stable for ever, never reused.
/// </summary>
public sealed class User : Entity
{
    /// <summary>Sign-in name in e-mail form, NFC, domain part as A-label lower case. Unique system-wide.</summary>
    public string UserPrincipalName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Mail { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public string? Office { get; set; }
    public string? MobilePhone { get; set; }
    public string? BusinessPhone { get; set; }
    public Guid? ManagerId { get; set; }
    public string? EmployeeId { get; set; }
    public string? PhotoKey { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Member;
    public Guid? HomeTenantId { get; set; }
    public string? InvitationState { get; set; }
    public UserSource Source { get; set; } = UserSource.Local;
    public string? ExternalId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    /// <summary>h12 or h23; null = from locale.</summary>
    public string? HourCycle { get; set; }
    public string? UsageLocation { get; set; }
    public DateTimeOffset? PasswordLastChangedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    /// <summary>Changes on password change / revocation: older sessions and cookies stop working.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? DeletedRestoreUntil { get; set; }
}

public enum AddressType { Primary, Alias }

/// <summary>Mail address of a user; unique system-wide, only in verified domains of the tenant.</summary>
public sealed class UserAddress : Entity
{
    public Guid UserId { get; set; }
    public string Address { get; set; } = "";
    public AddressType Type { get; set; }
}

/// <summary>One row per directory change, for delta queries. change_seq grows per tenant.</summary>
public sealed class ChangeLogEntry
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public string Change { get; set; } = "";
    public long ChangeSeq { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Event written in the same transaction as the change (outbox); delivered by the worker (Phase 5).</summary>
public sealed class OutboxEvent
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ResourceId { get; set; }
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public long SubjectSeq { get; set; }
    public Guid Txn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Per-subject event counter (bid_seq of docs/05 7.1).</summary>
public sealed class SubjectSequence
{
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public long LastSeq { get; set; }
}

/// <summary>Per-tenant counter behind change_log.change_seq.</summary>
public sealed class TenantSequence
{
    public Guid TenantId { get; set; }
    public long LastSeq { get; set; }
}

/// <summary>Password hash with algorithm prefix (docs/00 D1). Only the newest non-deleted row is current; older rows are history.</summary>
public sealed class PasswordRecord : Entity
{
    public Guid UserId { get; set; }
    public string Hash { get; set; } = "";
    public bool MustChange { get; set; }
}

/// <summary>Browser session of BlueID.</summary>
public sealed class Session : Entity
{
    public Guid UserId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceLabel { get; set; }
    /// <summary>jsonb array of RFC 8176 values.</summary>
    public string Amr { get; set; } = "[]";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}

public enum SignInResult { Success, Failure, Blocked, Interrupted }

public sealed class SignInLog
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>SHA-256 of the normalised name typed, so failed attempts can be grouped without storing it.</summary>
    public byte[] UpnAttemptedHash { get; set; } = [];
    public string? ClientId { get; set; }
    public string? Resource { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Country { get; set; }
    public SignInResult Result { get; set; }
    public string? FailureCode { get; set; }
    public bool MfaUsed { get; set; }
    public string ConditionalAccess { get; set; } = "{}";
    public string Risk { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Soft lockout counter per account (key "u:&lt;id&gt;") or per IP (key "ip:&lt;addr&gt;").</summary>
public sealed class Lockout
{
    public string Key { get; set; } = "";
    public Guid TenantId { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset? Until { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AdminScope { Tenant, OrgUnit }

/// <summary>Administrative role definition (built-in or registered by a product).</summary>
public sealed class AdminRole
{
    public string Code { get; set; } = "";
    /// <summary>jsonb array of permission codes.</summary>
    public string Permissions { get; set; } = "[]";
    public bool BuiltIn { get; set; }
}

public sealed class AdminRoleAssignment : Entity
{
    public string RoleCode { get; set; } = "";
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public AdminScope Scope { get; set; } = AdminScope.Tenant;
    public Guid? ScopeId { get; set; }
}

public enum AuditResult { Success, Failure, Denied }

/// <summary>Append-only, hash-chained (prev_hash, hash).</summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string Action { get; set; } = "";
    public string? Entity { get; set; }
    public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public AuditResult Result { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public byte[] PrevHash { get; set; } = [];
    public byte[] Hash { get; set; } = [];
}
