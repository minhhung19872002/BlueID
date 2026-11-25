using System.Net;
using System.Text.Json;
using BlueID.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BlueID.IntegrationTests;

[Collection(StackCollection.Name)]
public sealed class Phase2Tests(StackFixture stack)
{
    private readonly OAuthHelper oauth = new(stack);

    [Fact]
    public async Task Discovery_lists_only_what_the_contract_allows()
    {
        using var c = stack.Web.CreateClient();
        var d = JsonDocument.Parse(await c.GetStringAsync("/.well-known/openid-configuration")).RootElement;
        d.GetProperty("issuer").GetString().Should().Be(StackFixture.Issuer);
        // Endpoint URLs follow the request host (Caddy sends the public one); the issuer is fixed.
        d.GetProperty("jwks_uri").GetString().Should().EndWith("/.well-known/jwks.json");
        Strings(d, "code_challenge_methods_supported").Should().Equal("S256");
        Strings(d, "response_types_supported").Should().Equal("code");
        Strings(d, "response_modes_supported").Should().BeEquivalentTo("query", "form_post");
        Strings(d, "grant_types_supported").Should().BeEquivalentTo("authorization_code", "refresh_token", "client_credentials");
        Strings(d, "token_endpoint_auth_methods_supported").Should().NotContain(["client_secret_post", "client_secret_jwt"]).And.Contain("private_key_jwt");
        d.GetProperty("backchannel_logout_supported").GetBoolean().Should().BeTrue();
        d.GetProperty("end_session_endpoint").GetString().Should().EndWith("/connect/logout");
        // RFC 8414 metadata at its own well-known path, same content.
        var asMeta = JsonDocument.Parse(await c.GetStringAsync("/.well-known/oauth-authorization-server")).RootElement;
        asMeta.GetProperty("issuer").GetString().Should().Be(StackFixture.Issuer);

        var jwks = JsonDocument.Parse(await c.GetStringAsync("/.well-known/jwks.json")).RootElement.GetProperty("keys").EnumerateArray()
            .Select(k => k.GetProperty("kid").GetString()).ToList();
        var ring = stack.Web.Services.GetRequiredService<KeyRing>();
        jwks.Should().Contain(ring.Keys.Select(k => k.Kid), "ACTIVE and NEXT keys are published");
        ring.Keys.Should().Contain(k => k.Status == Domain.SigningKeyStatus.Next);
    }

    [Fact]
    public async Task Code_flow_with_pkce_gives_contract_tokens_for_one_resource()
    {
        await oauth.EnsureClientsAsync();
        var cookie = await stack.CookieOfAsync("admin@acme.test");
        var t = await oauth.SignInAndGetTokensAsync(cookie);
        t.Status.Should().Be(HttpStatusCode.OK, t.Body.ToString());
        var at = OAuthHelper.Read(t.AccessToken!);
        at.Typ.Should().Be("at+jwt");
        at.Alg.Should().Be("RS256");
        at.Kid.Should().Be(stack.Web.Services.GetRequiredService<KeyRing>().Active.Kid);
        at.Audiences.Should().Equal(OAuthHelper.Api);
        at.Issuer.Should().Be(StackFixture.Issuer);
        Claim(at, "idtyp").Should().Be("user");
        Claim(at, "ver").Should().Be("1.0");
        Claim(at, "scope").Split(' ').Should().Contain("t.read");
        Claim(at, "scp").Should().Be("t.read");
        Claim(at, "preferred_username").Should().Be("admin@acme.test");
        Claim(at, "acct").Should().Be("member");
        Guid.TryParse(Claim(at, "tid"), out _).Should().BeTrue();
        Guid.TryParse(Claim(at, "sid"), out _).Should().BeTrue();
        at.Claims.Select(c => c.Type).Should().Contain("amr");
        // Nothing outside the contract (docs/05 5.3) is in the token.
        var allowed = new[] { "iss", "aud", "exp", "iat", "nbf", "jti", "sub", "tid", "sid", "acct", "home_tid", "idtyp", "amr", "acr", "auth_time", "ver",
            "name", "given_name", "family_name", "preferred_username", "email", "email_verified", "locale", "zoneinfo", "hour_cycle", "scope", "scp", "client_id",
            "roles", "lic", "groups", "hasgroups", "xms_cc", "azp" };
        // oi_* claims are the server's own bookkeeping (docs/05 5.3: products must not use them).
        at.Claims.Select(c => c.Type).Distinct().Where(t => !t.StartsWith("oi_", StringComparison.Ordinal)).Except(allowed).Should().BeEmpty();

        var id = OAuthHelper.Read(t.IdToken!);
        id.Audiences.Should().Equal("t-spa");
        Claim(id, "sid").Should().Be(Claim(at, "sid"));
        Claim(id, "email").Should().Be("admin@acme.test");
        t.RefreshToken.Should().NotBeNullOrEmpty();

        using var c = stack.Web.CreateClient();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t.AccessToken);
        var ui = JsonDocument.Parse(await c.GetStringAsync("/connect/userinfo")).RootElement;
        ui.GetProperty("sub").GetString().Should().Be(at.Subject);
        ui.GetProperty("email").GetString().Should().Be("admin@acme.test");
    }

    [Fact]
    public async Task Client_credentials_gives_an_application_token()
    {
        await oauth.EnsureClientsAsync();
        var t = await oauth.TokenAsync(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "t.read", ["resource"] = OAuthHelper.Api },
            ("t-conf", OAuthHelper.Secret));
        t.Status.Should().Be(HttpStatusCode.OK, t.Body.ToString());
        var at = OAuthHelper.Read(t.AccessToken!);
        Claim(at, "idtyp").Should().Be("app");
        at.Audiences.Should().Equal(OAuthHelper.Api);
        t.RefreshToken.Should().BeNull();
    }

    private static string[] Strings(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static string Claim(JsonWebToken t, string type) => t.Claims.First(c => c.Type == type).Value;
}
