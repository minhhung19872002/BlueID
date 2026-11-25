using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlueID.Application.OAuth;
using BlueID.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.IntegrationTests;

public sealed record TokenResult(HttpStatusCode Status, JsonElement Body)
{
    public string? AccessToken => Body.TryGetProperty("access_token", out var v) ? v.GetString() : null;
    public string? RefreshToken => Body.TryGetProperty("refresh_token", out var v) ? v.GetString() : null;
    public string? IdToken => Body.TryGetProperty("id_token", out var v) ? v.GetString() : null;
    public string? Error => Body.TryGetProperty("error", out var v) ? v.GetString() : null;
}

/// <summary>Drives OAuth flows against the BlueID test server the way a client would (no browser).</summary>
public sealed class OAuthHelper(StackFixture stack)
{
    public const string Api = "https://api.oauthtest.test";
    public const string Api2 = "https://api2.oauthtest.test";
    public const string Redirect = "http://app.test/cb";
    public const string Secret = "test-client-secret-0123456789";
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static bool _registered;

    /// <summary>Test resources and clients (idempotent, through the same registry production uses).</summary>
    public async Task EnsureClientsAsync()
    {
        await Once.WaitAsync();
        try
        {
            if (_registered)
            {
                return;
            }

            await using var scope = stack.WebScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<Infrastructure.Persistence.BlueIdDbContext>();
            var platform = db.IgnoreQueryFiltersFirst(TenantType.Platform);
            var acme = db.IgnoreQueryFiltersBySlug("acme");
            var reg = sp.GetRequiredService<ApplicationRegistry>();
            await reg.EnsureResourceAsync(platform, new ResourceDefinition(Api, "OAuth test API", [new("t.read", "Read test data"), new("t.write", "Write test data")]), default);
            await reg.EnsureResourceAsync(platform, new ResourceDefinition(Api2, "Other API", [new("o.read", "Read other data")]), default);
            string[] grants = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];
            string[] scopes = [.. ApplicationRegistry.OidcScopes, "t.read", "t.write", "o.read"];
            await reg.EnsureClientAsync(platform, new ClientDefinition("t-spa", "Test SPA", ApplicationKind.Public, [Redirect], ["http://app.test/bye"], grants,
                [Api, Api2], scopes, FirstParty: true, MultiTenant: true), default);
            await reg.EnsureClientAsync(platform, new ClientDefinition("t-conf", "Test server app", ApplicationKind.Confidential, [Redirect], [],
                [.. grants, GrantTypes.ClientCredentials], [Api, Api2], scopes, ClientSecret: Secret, FirstParty: true, MultiTenant: true), default);
            await reg.EnsureClientAsync(acme, new ClientDefinition("t-third", "Acme Expense Tracker", ApplicationKind.Public, [Redirect], [], grants,
                [Api], scopes), default);
            _registered = true;
        }
        finally
        {
            Once.Release();
        }
    }

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>GET /connect/authorize with the session cookie; returns the status and the Location header (redirects are not followed).</summary>
    public async Task<(HttpStatusCode Status, string? Location, string Body)> AuthorizeAsync(string? cookie, IDictionary<string, string?> query)
    {
        using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/authorize", query));
        if (cookie is not null)
        {
            req.Headers.Add("Cookie", cookie);
        }

        var res = await c.SendAsync(req);
        return (res.StatusCode, res.Headers.Location?.ToString(), await res.Content.ReadAsStringAsync());
    }

    public static Dictionary<string, string?> CodeRequest(string clientId, string scope, string? resource, string? challenge, string method = "S256",
        string redirect = Redirect) => new()
    {
        ["client_id"] = clientId,
        ["response_type"] = "code",
        ["redirect_uri"] = redirect,
        ["scope"] = scope,
        ["state"] = "st-" + Guid.NewGuid().ToString("N")[..8],
        ["nonce"] = "n-" + Guid.NewGuid().ToString("N")[..8],
        ["resource"] = resource,
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = challenge is null ? null : method,
    };

    /// <summary>
    /// The OAuth error of an authorize answer: in the redirect (most errors) or in the error page OpenIddict renders when it
    /// does not redirect (e.g. missing PKCE). "none" when a code was issued.
    /// </summary>
    public static string ErrorOf((HttpStatusCode Status, string? Location, string Body) a)
    {
        if (a.Location is { } loc && Uri.TryCreate(loc, UriKind.Absolute, out _))
        {
            var q = QueryOf(loc);
            return q.TryGetValue("error", out var e) ? e : q.ContainsKey("code") ? "none" : "no-error-in-redirect";
        }

        var m = System.Text.RegularExpressions.Regex.Match(a.Body, "error:([a-z_]+)");
        return m.Success ? m.Groups[1].Value : $"status {(int)a.Status}";
    }

    public static Dictionary<string, string> QueryOf(string location) =>
        QueryHelpers.ParseQuery(new Uri(location, UriKind.Absolute).Query).ToDictionary(k => k.Key, k => k.Value.ToString());

    public async Task<TokenResult> TokenAsync(IDictionary<string, string> form, (string Id, string Secret)? basic = null)
    {
        using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(form) };
        if (basic is { } b)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(b.Id) + ":" + Uri.EscapeDataString(b.Secret))));
        }

        var res = await c.SendAsync(req);
        return new TokenResult(res.StatusCode, JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    /// <summary>Full code + PKCE flow for a public client; returns the token response.</summary>
    public async Task<TokenResult> SignInAndGetTokensAsync(string cookie, string scope = "openid profile email offline_access t.read", string? resource = Api,
        string clientId = "t-spa")
    {
        var (verifier, challenge) = Pkce();
        var a = await AuthorizeAsync(cookie, CodeRequest(clientId, scope, resource, challenge));
        a.Status.Should().Be(HttpStatusCode.Redirect, a.Body);
        var q = QueryOf(a.Location!);
        q.Should().ContainKey("code", a.Location);
        return await TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = q["code"], ["redirect_uri"] = Redirect, ["client_id"] = clientId, ["code_verifier"] = verifier,
            ["resource"] = resource ?? "",
        }.Where(kv => kv.Value.Length > 0).ToDictionary());
    }

    public Task<TokenResult> RefreshAsync(string refreshToken, string clientId = "t-spa") =>
        TokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = clientId });

    public async Task<JsonElement> IntrospectAsync(string token)
    {
        using var c = stack.Web.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/connect/introspect") { Content = new FormUrlEncodedContent([new("token", token)]) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("t-conf:" + Secret)));
        var res = await c.SendAsync(req);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public static JsonWebToken Read(string jwt) => new(jwt);
}

internal static class TenantLookup
{
    public static Guid IgnoreQueryFiltersFirst(this Infrastructure.Persistence.BlueIdDbContext db, TenantType type) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Tenants).First(t => t.Type == type).Id;

    public static Guid IgnoreQueryFiltersBySlug(this Infrastructure.Persistence.BlueIdDbContext db, string slug) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.Tenants).First(t => t.Slug == slug).Id;
}
