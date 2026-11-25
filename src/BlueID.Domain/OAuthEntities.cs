namespace BlueID.Domain;

public enum SigningKeyStatus { Next, Active, Retiring, Retired }

/// <summary>
/// Token signing key (PROMPT 7): published in JWKS while NEXT (at least 48 h before use), ACTIVE (signs) and RETIRING
/// (until every token it signed has expired). The private key is encrypted at rest with the master key.
/// Platform-level: not tenant-owned.
/// </summary>
public sealed class SigningKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kid { get; set; } = "";
    public string Algorithm { get; set; } = "RS256";
    public byte[] PrivateKeyEncrypted { get; set; } = [];
    public string PublicJwk { get; set; } = "{}";
    public SigningKeyStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? RetireAt { get; set; }
}

public enum GroupClaims { None, Security, All }

/// <summary>A resource server (a product): scopes, app roles, licence SKUs, event endpoint (docs/05 3.1).</summary>
public sealed class ApiResource : Entity
{
    /// <summary>Absolute URI, the "aud" of its access tokens. Unique system-wide.</summary>
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>jsonb: [{ value, description, adminConsentRequired }].</summary>
    public string Scopes { get; set; } = "[]";
    /// <summary>jsonb: [{ value, displayName, allowedMemberTypes }].</summary>
    public string AppRoles { get; set; } = "[]";
    /// <summary>jsonb: ["mail.standard", ...].</summary>
    public string LicenseSkus { get; set; } = "[]";
    public string? EventEndpoint { get; set; }
    /// <summary>jsonb: { type: none|bearer, token }.</summary>
    public string EventAuth { get; set; } = "{}";
    public string TokenLifetimeOverrides { get; set; } = "{}";
    public GroupClaims GroupClaims { get; set; } = GroupClaims.None;
}

public enum ApplicationKind { Confidential, Public }

/// <summary>
/// BlueID's record of an OAuth client (PROMPT 4 apps.applications). The protocol side (secrets, redirect URIs,
/// permissions) lives in OpenIddict's oauth.applications under the same client_id; both are written together by
/// the application registry.
/// </summary>
public sealed class ClientApplication : Entity
{
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Publisher { get; set; }
    public ApplicationKind Kind { get; set; }
    public bool MultiTenant { get; set; }
    public bool FirstParty { get; set; }
    public bool RequirePar { get; set; }
    public string? BackchannelLogoutUri { get; set; }
    /// <summary>docs/00 D14: exempt from the PKCE requirement; only honoured when BID_CONFORMANCE_MODE=true.</summary>
    public bool ConformanceTestClient { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Consent of a user (or of an administrator for the whole tenant when UserId is null).</summary>
public sealed class Consent : Entity
{
    public Guid ApplicationId { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>jsonb array of granted scopes (OIDC scopes and resource scopes).</summary>
    public string Scopes { get; set; } = "[]";
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Which applications obtained tokens through a browser session: targets of back-channel logout.</summary>
public sealed class SessionClient
{
    public Guid SessionId { get; set; }
    public string ClientId { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset FirstAt { get; set; }
}

/// <summary>
/// Single-use values (PROMPT 3, lesson 1): a refresh token is exchanged once, a client assertion jti is accepted once.
/// The primary key is the judge, so truly parallel requests have exactly one winner.
/// </summary>
public sealed class UsedToken
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public enum DeliveryStatus { Pending, Delivered, Failed }

/// <summary>A back-channel logout token to deliver (OIDC Back-Channel Logout 1.0), retried with back-off.</summary>
public sealed class LogoutDelivery
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string ClientId { get; set; } = "";
    public string Uri { get; set; } = "";
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
