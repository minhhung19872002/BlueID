using System.Security.Claims;
using BlueID.Application.Auth;
using BlueID.Application.OAuth;
using BlueID.Application.Web;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.Web.Controllers;

/// <summary>
/// OIDC / OAuth endpoints in OpenIddict pass-through mode: OpenIddict validates the protocol (client, redirect URI, PKCE,
/// grant permissions); this controller decides who the user is, what the token says and whether it may be issued.
/// Public by nature: each action validates its own request (lesson 3).
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class OidcController(
    BlueIdDbContext db,
    IOpenIddictScopeManager scopeManager,
    IOpenIddictTokenManager tokenManager,
    IOpenIddictAuthorizationManager authorizationManager,
    SessionService sessions,
    BackchannelLogoutQueue backchannel,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    TimeProvider clock) : Controller
{
    private Microsoft.AspNetCore.DataProtection.ITimeLimitedDataProtector Reauth =>
        Microsoft.AspNetCore.DataProtection.DataProtectionAdvancedExtensions.ToTimeLimitedDataProtector(protection.CreateProtector("blueid.reauth"));

    private const string ReauthMarker = "bid_reauth";
    public const string DeniedMarker = "bid_denied";
    public static readonly TimeSpan MaxRefreshAge = TimeSpan.FromDays(90);

    [AllowAnonymous]
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID Connect request.");
        if (request.Request is not null)
        {
            // OIDC Core 6.1: request objects are not supported; the client gets request_not_supported (BUG-014), delivered
            // the way it asked. Only response_mode is read from the (unverified) object, and only to choose between the two
            // supported modes; the redirect URI is the one OpenIddict validated from the outer parameters.
            if (request.ResponseMode is null && RequestObjectResponseMode(request.Request) is { } mode)
            {
                request.ResponseMode = mode;
            }

            return Fail(Errors.RequestNotSupported, "Request objects are not supported.");
        }

        var app = await db.ClientApplications.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.DeletedAt == null, ct);
        if (app is null || !app.Enabled)
        {
            return Fail(Errors.UnauthorizedClient, "The application is disabled.");
        }

        var (resource, targetError) = await ValidateTargetAsync(request.GetScopes(), request.GetResources(), ct);
        if (targetError is not null)
        {
            return targetError;
        }

        // Who is signed in: the BlueID session cookie (validated against auth.sessions on every request).
        var cookie = await HttpContext.AuthenticateAsync(SessionCookie.Scheme);
        var session = cookie.Succeeded && Guid.TryParse(cookie.Principal!.FindFirstValue(BidClaims.Session), out var sid)
            ? await db.Sessions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct)
            : null;
        // The marker is signed and carries the moment BlueID sent the browser to sign in: it only counts for a session
        // created after that moment, so neither copying nor forging it skips prompt=login / max_age (BUG-009).
        var reauthenticated = session is not null && ReauthTime(Request.Query[ReauthMarker].ToString()) is { } askedAt && session.CreatedAt >= askedAt;
        if (Request.Query.ContainsKey(DeniedMarker) && session is not null)
        {
            return Fail(Errors.AccessDenied, "The user declined.");
        }
        var tooOld = session is not null && request.MaxAge is { } maxAge && clock.GetUtcNow() - session.CreatedAt > TimeSpan.FromSeconds(maxAge) && !reauthenticated;
        var forceLogin = request.HasPromptValue(PromptValues.Login) && !reauthenticated;
        if (session is null || forceLogin || tooOld)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Fail(Errors.LoginRequired, "The user is not signed in.");
            }

            return Redirect(LoginUrl(request));
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == session.UserId, ct);
        if (!app.MultiTenant && app.TenantId != user.TenantId)
        {
            return Fail(Errors.AccessDenied, "The application is not available to this organisation.");
        }

        var scopes = request.GetScopes().ToList();
        if (!app.FirstParty)
        {
            var granted = await GrantedScopesAsync(app, user, ct);
            var needsConsent = request.HasPromptValue(PromptValues.Consent) || !scopes.All(granted.Contains);
            if (needsConsent)
            {
                if (request.HasPromptValue(PromptValues.None))
                {
                    return Fail(Errors.ConsentRequired, "The user has not consented.");
                }

                return Redirect("/consent?ReturnUrl=" + Uri.EscapeDataString(AuthorizeUrlWithout(PromptValues.Consent)));
            }
        }

        await backchannel.RecordClientAsync(session.Id, user.TenantId, user.Id, app.ClientId, ct);
        var principal = TokenClaims.ForUser(user, new TokenClaims.SessionFacts(session.Id, session.CreatedAt, TokenClaims.AmrOf(session)), scopes, resource);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID Connect request.");
        var app = await db.ClientApplications.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.ClientId == request.ClientId && a.DeletedAt == null, ct);
        if (app is null || !app.Enabled)
        {
            return Fail(Errors.UnauthorizedClient, "The application is disabled.");
        }

        // A client assertion is accepted once (docs/05 4): its jti is recorded until it expires.
        if (!string.IsNullOrEmpty(request.ClientAssertion))
        {
            var assertion = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(request.ClientAssertion);
            if (string.IsNullOrEmpty(assertion.Id) || !await UseOnceAsync("assertion", app.ClientId + ":" + assertion.Id, assertion.ValidTo, ct))
            {
                return Fail(Errors.InvalidClient, "The client assertion was already used.");
            }
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var stored = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            // Exactly one exchange per refresh token even under truly parallel requests: the database decides; the losers
            // are a reuse and the whole chain (authorization) is revoked (PROMPT 3).
            if (request.IsRefreshTokenGrantType() && stored?.GetTokenId() is { } tokenId &&
                !await UseOnceAsync("refresh", tokenId, clock.GetUtcNow().AddDays(15), ct))
            {
                if (stored.GetAuthorizationId() is { } authorizationId)
                {
                    await tokenManager.RevokeByAuthorizationIdAsync(authorizationId, ct);
                    if (await authorizationManager.FindByIdAsync(authorizationId, ct) is { } authorization)
                    {
                        await authorizationManager.TryRevokeAsync(authorization, ct);
                    }
                }

                return Fail(Errors.InvalidGrant, "The refresh token was already used.");
            }

            if (stored is null || !Guid.TryParse(stored.GetClaim(Claims.Subject), out var userId) || !Guid.TryParse(stored.GetClaim(TokenClaims.Session), out var sessionId))
            {
                return Fail(Errors.InvalidGrant, "The grant is incomplete.");
            }

            // Every code redemption and refresh asks again: the session must still be live, the user and tenant active
            // (the same rule as the browser, lesson 8).
            var session = await db.Sessions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (session is null || user is null || !(await sessions.ValidateAsync(userId, sessionId, user.SecurityStamp, ct)).IsValid)
            {
                return Fail(Errors.InvalidGrant, "The session is no longer valid.");
            }

            // docs/05 5.4: refresh tokens slide 14 days but never outlive 90 days from the sign-in.
            if (request.IsRefreshTokenGrantType() && clock.GetUtcNow() - session.CreatedAt > MaxRefreshAge)
            {
                return Fail(Errors.InvalidGrant, "Sign in again.");
            }

            var resource = stored.GetResources().FirstOrDefault();
            var principal = TokenClaims.ForUser(user, new TokenClaims.SessionFacts(session.Id, session.CreatedAt, TokenClaims.AmrOf(session)), stored.GetScopes(), resource);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            if (app.Kind != ApplicationKind.Confidential)
            {
                return Fail(Errors.UnauthorizedClient, "Only confidential applications act as themselves.");
            }

            var scopes = request.GetScopes().ToList();
            var (resource, targetError) = await ValidateTargetAsync(scopes, request.GetResources(), ct);
            if (targetError is not null)
            {
                return targetError;
            }

            if (resource is null || scopes.Any(s => ApplicationRegistry.OidcScopes.Contains(s, StringComparer.Ordinal)))
            {
                return Fail(Errors.InvalidTarget, "Client credentials need a resource and only that resource's scopes.");
            }

            return SignIn(TokenClaims.ForApplication(app, scopes, resource), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Fail(Errors.UnsupportedGrantType, "The grant type is not supported.");
    }

    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo(CancellationToken ct)
    {
        var token = User;
        if (!Guid.TryParse(token.GetClaim(Claims.Subject), out var userId) || !Guid.TryParse(token.GetClaim(TokenClaims.Session), out var sessionId))
        {
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !(await sessions.ValidateAsync(userId, sessionId, user.SecurityStamp, ct)).IsValid)
        {
            return Challenge(new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The session is no longer valid.",
            }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var scopes = token.GetScopes().ToHashSet(StringComparer.Ordinal);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal) { [Claims.Subject] = user.Id.ToString("D"), [TokenClaims.Tenant] = user.TenantId.ToString("D") };
        if (scopes.Contains(Scopes.Profile))
        {
            claims[Claims.Name] = user.DisplayName;
            claims[Claims.PreferredUsername] = user.UserPrincipalName;
            AddIf(claims, Claims.GivenName, user.GivenName);
            AddIf(claims, Claims.FamilyName, user.FamilyName);
            claims[Claims.UpdatedAt] = user.UpdatedAt.ToUnixTimeSeconds();
        }

        if (scopes.Contains(Scopes.Profile) || scopes.Contains("bid.locale"))
        {
            AddIf(claims, Claims.Locale, user.Locale);
            AddIf(claims, Claims.Zoneinfo, user.TimeZone);
            AddIf(claims, TokenClaims.HourCycle, user.HourCycle);
        }

        if (scopes.Contains(Scopes.Email) && user.Mail is not null)
        {
            claims[Claims.Email] = user.Mail;
            claims[Claims.EmailVerified] = true;
        }

        return Ok(claims);
    }

    [AllowAnonymous]
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {

        // OpenIddict has checked post_logout_redirect_uri (exact match against some client). Only a valid id_token_hint
        // tells which client asks; without one (missing, modified, expired) the person stays on BlueID's signed-out page
        // and is never sent to a URI an attacker chose (BUG-016).
        var request = HttpContext.GetOpenIddictServerRequest();
        var hint = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var cookie = await HttpContext.AuthenticateAsync(SessionCookie.Scheme);
        if (cookie.Succeeded && Guid.TryParse(cookie.Principal!.FindFirstValue(BidClaims.Session), out var sid))
        {
            await sessions.EndAsync(sid, "signed_out", ct);
        }

        await HttpContext.SignOutAsync(SessionCookie.Scheme);
        // Without a hint OpenIddict still hands back an empty principal: only a subject counts.
        if (hint?.GetClaim(Claims.Subject) is null || string.IsNullOrEmpty(request?.PostLogoutRedirectUri))
        {
            return Redirect("/logout");
        }

        return SignOut(new AuthenticationProperties { RedirectUri = "/logout" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// docs/05 3.2: at most one resource; scopes other than OIDC ones all belong to it; resource scopes without a resource
    /// are refused (invalid_target), scopes of another resource too (invalid_scope).
    /// </summary>
    private async Task<(string? Resource, IActionResult? Error)> ValidateTargetAsync(IEnumerable<string> scopes, IEnumerable<string> resources, CancellationToken ct)
    {
        var resourceList = resources.Distinct(StringComparer.Ordinal).ToList();
        if (resourceList.Count > 1)
        {
            return (null, Fail(Errors.InvalidTarget, "Request one resource per token."));
        }

        var resource = resourceList.FirstOrDefault();
        if (resource is not null && !await db.ApiResources.IgnoreQueryFilters().AnyAsync(r => r.Identifier == resource && r.DeletedAt == null, ct))
        {
            return (null, Fail(Errors.InvalidTarget, "Unknown resource."));
        }

        foreach (var scope in scopes.Where(s => !ApplicationRegistry.OidcScopes.Contains(s, StringComparer.Ordinal)))
        {
            var entity = await scopeManager.FindByNameAsync(scope, ct);
            if (entity is null)
            {
                return (null, Fail(Errors.InvalidScope, $"Unknown scope {scope}."));
            }

            var owners = await scopeManager.GetResourcesAsync(entity, ct);
            if (resource is null)
            {
                return (null, Fail(Errors.InvalidTarget, "Resource scopes need the resource parameter (RFC 8707)."));
            }

            if (!owners.Contains(resource))
            {
                return (null, Fail(Errors.InvalidScope, $"Scope {scope} does not belong to the requested resource."));
            }
        }

        return (resource, null);
    }

    private async Task<bool> UseOnceAsync(string kind, string value, DateTimeOffset expiresAt, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO auth.used_tokens (kind, value, expires_at) VALUES ({kind}, {value}, {expiresAt})
            ON CONFLICT (kind, value) DO NOTHING
            """, ct) == 1;

    private async Task<HashSet<string>> GrantedScopesAsync(ClientApplication app, User user, CancellationToken ct)
    {
        var rows = await db.Consents.IgnoreQueryFilters()
            .Where(c => c.ApplicationId == app.Id && c.TenantId == user.TenantId && c.RevokedAt == null && c.DeletedAt == null && (c.UserId == null || c.UserId == user.Id))
            .Select(c => c.Scopes).ToListAsync(ct);
        return rows.SelectMany(s => System.Text.Json.JsonSerializer.Deserialize<string[]>(s) ?? []).ToHashSet(StringComparer.Ordinal);
    }

    private string LoginUrl(OpenIddictRequest request)
    {
        var marker = Reauth.Protect(clock.GetUtcNow().UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), clock.GetUtcNow().AddMinutes(10));
        var back = AuthorizeUrlWithout(PromptValues.Login) + "&" + ReauthMarker + "=" + Uri.EscapeDataString(marker);
        var url = "/login?ReturnUrl=" + Uri.EscapeDataString(back);
        if (!string.IsNullOrEmpty(request.LoginHint))
        {
            url += "&login_hint=" + Uri.EscapeDataString(request.LoginHint);
        }

        if (!string.IsNullOrEmpty(request.UiLocales))
        {
            url += "&ui_locales=" + Uri.EscapeDataString(request.UiLocales.Split(' ')[0]);
        }

        return url;
    }

    /// <summary>The authorize request as a local URL without the given prompt value and without our marker (no loops).</summary>
    private string AuthorizeUrlWithout(string prompt)
    {
        var pairs = (Request.HasFormContentType ? Request.Form.Select(f => (f.Key, f.Value.ToString())) : Request.Query.Select(q => (q.Key, q.Value.ToString())))
            .Where(p => p.Key != ReauthMarker && p.Key != DeniedMarker)
            .Select(p => p.Key == Parameters.Prompt ? (p.Key, string.Join(' ', p.Item2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(v => v != prompt))) : p)
            .Where(p => !(p.Key == Parameters.Prompt && p.Item2.Length == 0))
            .Select(p => new KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>(p.Key, p.Item2));
        return "/connect/authorize" + QueryString.Create(pairs);
    }

    private static string? RequestObjectResponseMode(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2 || parts[1].Length > 16_384)
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]));
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(Parameters.ResponseMode, out var m) && m.GetString() is ResponseModes.FormPost or ResponseModes.Query
                ? m.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    private DateTimeOffset? ReauthTime(string marker)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return null;
        }

        try
        {
            return long.TryParse(Reauth.Unprotect(marker), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private ForbidResult Fail(string error, string description) =>
        Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private static void AddIf(Dictionary<string, object> claims, string name, string? value)
    {
        if (value is not null)
        {
            claims[name] = value;
        }
    }
}
