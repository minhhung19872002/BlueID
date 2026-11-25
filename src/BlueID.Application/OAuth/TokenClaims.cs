using System.Security.Claims;
using System.Text.Json;
using BlueID.Domain;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.Application.OAuth;

/// <summary>
/// Claims of BlueID tokens: the integration contract (docs/05 5.3). Any change here is a contract change: docs/05 first,
/// MINOR bump, contract test.
/// </summary>
public static class TokenClaims
{
    public const string ContractVersion = "1.0";
    public const string Tenant = "tid";
    public const string Session = "sid";
    public const string AccountKind = "acct";
    public const string HomeTenant = "home_tid";
    public const string IdentityType = "idtyp";
    public const string HourCycle = "hour_cycle";
    public const string Version = "ver";
    public const string Licenses = "lic";
    public const string ResourceScopes = "scp";
    public const string AcrSingleFactor = "urn:blueid:acr:1fa";
    public const string AcrMultiFactor = "urn:blueid:acr:mfa";

    public sealed record SessionFacts(Guid SessionId, DateTimeOffset AuthTime, IReadOnlyList<string> Amr);

    /// <summary>Principal for a person (authorization code / refresh token grants).</summary>
    public static ClaimsPrincipal ForUser(User user, SessionFacts session, IEnumerable<string> scopes, string? resource)
    {
        var id = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        id.SetClaim(Claims.Subject, user.Id.ToString("D"))
            .SetClaim(Tenant, user.TenantId.ToString("D"))
            .SetClaim(Session, session.SessionId.ToString("D"))
            .SetClaim(AccountKind, user.AccountType == AccountType.Guest ? "guest" : "member")
            .SetClaim(IdentityType, "user")
            .SetClaim(Claims.AuthenticationTime, session.AuthTime.ToUnixTimeSeconds())
            .SetClaim(Claims.AuthenticationContextReference, session.Amr.Contains("mfa") ? AcrMultiFactor : AcrSingleFactor)
            .SetClaim(Version, ContractVersion);
        id.SetClaims(Claims.AuthenticationMethodReference, [.. session.Amr]);
        if (user.HomeTenantId is { } home)
        {
            id.SetClaim(HomeTenant, home.ToString("D"));
        }

        var scopeSet = scopes.ToHashSet(StringComparer.Ordinal);
        if (scopeSet.Contains(Scopes.Profile))
        {
            id.SetClaim(Claims.Name, user.DisplayName)
                .SetClaim(Claims.PreferredUsername, user.UserPrincipalName);
            if (user.GivenName is { } g)
            {
                id.SetClaim(Claims.GivenName, g);
            }

            if (user.FamilyName is { } f)
            {
                id.SetClaim(Claims.FamilyName, f);
            }
        }

        if (scopeSet.Contains(Scopes.Profile) || scopeSet.Contains("bid.locale"))
        {
            if (user.Locale is { } l)
            {
                id.SetClaim(Claims.Locale, l);
            }

            if (user.TimeZone is { } z)
            {
                id.SetClaim(Claims.Zoneinfo, z);
            }

            if (user.HourCycle is { } h)
            {
                id.SetClaim(HourCycle, h);
            }
        }

        if (scopeSet.Contains(Scopes.Email) && user.Mail is { } mail)
        {
            id.SetClaim(Claims.Email, mail).SetClaim(Claims.EmailVerified, true);
        }

        // docs/05 5.3: scp = the granted scopes of the audience resource only (OpenIddict adds RFC 9068 "scope" itself).
        var resourceScopes = scopeSet.Where(s => !ApplicationRegistry.OidcScopes.Contains(s, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (resourceScopes.Count > 0)
        {
            id.SetClaim(ResourceScopes, string.Join(' ', resourceScopes));
        }

        var principal = new ClaimsPrincipal(id);
        principal.SetScopes(scopeSet);
        if (resource is not null)
        {
            principal.SetResources(resource);
        }

        principal.SetDestinations(DestinationsOf);
        return principal;
    }

    /// <summary>Principal for an application acting as itself (client credentials): idtyp = app.</summary>
    public static ClaimsPrincipal ForApplication(ClientApplication app, IEnumerable<string> scopes, string resource)
    {
        var id = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        id.SetClaim(Claims.Subject, app.Id.ToString("D"))
            .SetClaim(Tenant, app.TenantId.ToString("D"))
            .SetClaim(IdentityType, "app")
            .SetClaim(Version, ContractVersion);
        var principal = new ClaimsPrincipal(id);
        principal.SetScopes(scopes);
        principal.SetResources(resource);
        principal.SetDestinations(_ => [Destinations.AccessToken]);
        return principal;
    }

    private static IEnumerable<string> DestinationsOf(Claim claim)
    {
        switch (claim.Type)
        {
            // Identity claims every token carries.
            case Claims.Subject or Tenant or Session or AccountKind or HomeTenant or Version or Claims.AuthenticationTime
                or Claims.AuthenticationContextReference or Claims.AuthenticationMethodReference:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;
            case IdentityType or ResourceScopes:
                yield return Destinations.AccessToken;
                break;
            case Claims.Name or Claims.PreferredUsername or Claims.GivenName or Claims.FamilyName:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;
            case Claims.Locale or Claims.Zoneinfo or HourCycle:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;
            case Claims.Email or Claims.EmailVerified:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                break;
            default:
                // Anything not in the contract stays out of tokens.
                yield break;
        }
    }

    public static IReadOnlyList<string> AmrOf(Session s) => JsonSerializer.Deserialize<string[]>(s.Amr) ?? [];
}
