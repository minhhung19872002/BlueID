using System.Text.Json;
using BlueID.Application.Common;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.Application.OAuth;

public sealed record ResourceScope(string Value, string Description, bool AdminConsentRequired = false);

public sealed record ResourceDefinition(string Identifier, string Name, IReadOnlyList<ResourceScope> Scopes, IReadOnlyList<string>? LicenseSkus = null,
    string? EventEndpoint = null);

public sealed record ClientDefinition(
    string ClientId,
    string Name,
    ApplicationKind Kind,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Scopes,
    string? ClientSecret = null,
    JsonWebKeySet? Jwks = null,
    bool FirstParty = false,
    bool MultiTenant = false,
    string? BackchannelLogoutUri = null,
    bool ConformanceTestClient = false,
    bool RequirePar = false,
    bool AllowClientSecretPost = false);

/// <summary>
/// The one way to register resources and clients: BlueID's apps.* rows and OpenIddict's oauth.* rows are written in one
/// transaction. Scope values are unique across every resource (the "resource" parameter then names the audience).
/// </summary>
public sealed class ApplicationRegistry(
    BlueIdDbContext db,
    IOpenIddictApplicationManager apps,
    IOpenIddictScopeManager scopes,
    IConfiguration config,
    ILogger<ApplicationRegistry> log)
{
    public static readonly string[] OidcScopes = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess, "bid.locale"];

    /// <summary>docs/00 D14: the conformance exemption exists only in processes started with BID_CONFORMANCE_MODE=true.</summary>
    public bool ConformanceMode => string.Equals(config["BID_CONFORMANCE_MODE"], "true", StringComparison.OrdinalIgnoreCase);

    public async Task<ApiResource> EnsureResourceAsync(Guid ownerTenantId, ResourceDefinition d, CancellationToken ct)
    {
        if (!Uri.TryCreate(d.Identifier, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ValidationFailedException([new FieldError("identifier", "resource_identifier_invalid")]);
        }

        var row = await db.ApiResources.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Identifier == d.Identifier && r.DeletedAt == null, ct);
        if (row is null)
        {
            row = new ApiResource { TenantId = ownerTenantId, Identifier = d.Identifier };
            db.ApiResources.Add(row);
        }

        row.Name = d.Name;
        row.Scopes = JsonSerializer.Serialize(d.Scopes.Select(s => new { value = s.Value, description = s.Description, adminConsentRequired = s.AdminConsentRequired }));
        row.LicenseSkus = JsonSerializer.Serialize(d.LicenseSkus ?? []);
        row.EventEndpoint = d.EventEndpoint;
        foreach (var s in d.Scopes)
        {
            if (OidcScopes.Contains(s.Value, StringComparer.Ordinal))
            {
                throw new ValidationFailedException([new FieldError("scopes", "scope_reserved")]);
            }

            var existing = await scopes.FindByNameAsync(s.Value, ct);
            if (existing is null)
            {
                await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = s.Value, Description = s.Description, Resources = { d.Identifier } }, ct);
            }
            else
            {
                var owners = await scopes.GetResourcesAsync(existing, ct);
                if (!owners.All(o => o == d.Identifier))
                {
                    throw AppException.Conflict("scope_taken");
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<ClientApplication> EnsureClientAsync(Guid ownerTenantId, ClientDefinition d, CancellationToken ct)
    {
        if (d.ConformanceTestClient && !ConformanceMode)
        {
            throw AppException.Forbidden("conformance_mode_off");
        }

        foreach (var u in d.RedirectUris.Concat(d.PostLogoutRedirectUris))
        {
            // Exact registered URIs only; no wildcards, no fragments (RFC 6749 3.1.2).
            if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) || !string.IsNullOrEmpty(parsed.Fragment) || u.Contains('*', StringComparison.Ordinal))
            {
                throw new ValidationFailedException([new FieldError("redirectUris", "redirect_uri_invalid")]);
            }
        }

        var row = await db.ClientApplications.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.ClientId == d.ClientId && a.DeletedAt == null, ct);
        if (row is null)
        {
            row = new ClientApplication { TenantId = ownerTenantId, ClientId = d.ClientId };
            db.ClientApplications.Add(row);
        }

        row.Name = d.Name;
        row.Kind = d.Kind;
        row.FirstParty = d.FirstParty;
        row.MultiTenant = d.MultiTenant;
        if (row.BackchannelLogoutUri != d.BackchannelLogoutUri)
        {
            // Pending logout tokens for the old URI are not retried against it any more (BUG-017).
            await db.LogoutDeliveries.Where(x => x.ClientId == d.ClientId && x.Status == DeliveryStatus.Pending)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.Status, DeliveryStatus.Failed).SetProperty(v => v.LastError, "backchannel_logout_uri changed"), ct);
        }

        row.BackchannelLogoutUri = d.BackchannelLogoutUri;
        row.ConformanceTestClient = d.ConformanceTestClient;
        row.RequirePar = d.RequirePar;
        row.AllowClientSecretPost = d.AllowClientSecretPost;
        row.Enabled = true;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = d.ClientId,
            DisplayName = d.Name,
            ClientType = d.Kind == ApplicationKind.Public ? ClientTypes.Public : ClientTypes.Confidential,
            ClientSecret = d.ClientSecret,
            JsonWebKeySet = d.Jwks,
            // Consent is BlueID's own (apps.consents); OpenIddict does not ask.
            ConsentType = ConsentTypes.Explicit,
        };
        foreach (var u in d.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(u));
        }

        foreach (var u in d.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(u));
        }

        descriptor.Permissions.UnionWith([Permissions.Endpoints.Token, Permissions.Endpoints.Revocation, Permissions.Endpoints.Introspection]);
        if (d.GrantTypes.Contains(GrantTypes.AuthorizationCode))
        {
            descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.EndSession, Permissions.Endpoints.PushedAuthorization,
                Permissions.ResponseTypes.Code]);
        }

        foreach (var g in d.GrantTypes)
        {
            // Implicit and password grants are never granted, whatever a definition says (PROMPT 3).
            if (g is GrantTypes.Implicit or GrantTypes.Password)
            {
                throw new ValidationFailedException([new FieldError("grantTypes", "grant_type_forbidden")]);
            }

            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + g);
        }

        foreach (var s in d.Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + s);
        }

        foreach (var r in d.Resources)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Resource + r);
        }

        if (!d.ConformanceTestClient)
        {
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        if (d.RequirePar)
        {
            descriptor.Requirements.Add(Requirements.Features.PushedAuthorizationRequests);
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var existing = await apps.FindByClientIdAsync(d.ClientId, ct);
        if (existing is null)
        {
            await apps.CreateAsync(descriptor, ct);
        }
        else
        {
            if (d.ClientSecret is null && d.Kind == ApplicationKind.Confidential)
            {
                // Keep the stored secret hash when the definition does not carry a new secret.
                var current = new OpenIddictApplicationDescriptor();
                await apps.PopulateAsync(current, existing, ct);
                descriptor.ClientSecret = current.ClientSecret;
            }

            await apps.UpdateAsync(existing, descriptor, ct);
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return row;
    }

    /// <summary>
    /// docs/00 D14 at start: without BID_CONFORMANCE_MODE=true every conformance client is disabled and gets the PKCE
    /// requirement back, so a dev database copied to production opens nothing.
    /// </summary>
    public async Task EnforceConformanceModeAsync(CancellationToken ct)
    {
        if (ConformanceMode)
        {
            log.LogWarning("BID_CONFORMANCE_MODE=true: conformance-suite clients are exempt from PKCE. Never set this in production.");
            return;
        }

        var flagged = await db.ClientApplications.IgnoreQueryFilters().Where(a => a.ConformanceTestClient && a.Enabled).ToListAsync(ct);
        foreach (var a in flagged)
        {
            a.Enabled = false;
            if (await apps.FindByClientIdAsync(a.ClientId, ct) is { } app)
            {
                var descriptor = new OpenIddictApplicationDescriptor();
                await apps.PopulateAsync(descriptor, app, ct);
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                await apps.UpdateAsync(app, descriptor, ct);
            }

            log.LogWarning("Conformance client {ClientId} disabled: BID_CONFORMANCE_MODE is not set", a.ClientId);
        }

        await db.SaveChangesAsync(ct);
    }
}
