using System.Net;
using System.Security.Cryptography;
using System.Text;
using BlueID.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BlueID.IntegrationTests;

/// <summary>PROMPT 10: one negative test per forbidden thing, plus the product-side checks of docs/05 5.2.</summary>
[Collection(StackCollection.Name)]
public sealed class Phase2NegativeTests(StackFixture stack)
{
    private readonly OAuthHelper oauth = new(stack);

    private async Task<string> CookieAsync()
    {
        await oauth.EnsureClientsAsync();
        return await stack.CookieOfAsync("admin@bluemail.test");
    }

    [Theory]
    [InlineData("token")]
    [InlineData("id_token")]
    [InlineData("id_token token")]
    [InlineData("code id_token")]
    [InlineData("code token")]
    public async Task Implicit_and_hybrid_are_refused(string responseType)
    {
        var cookie = await CookieAsync();
        var (_, challenge) = OAuthHelper.Pkce();
        var q = OAuthHelper.CodeRequest("t-spa", "openid", null, challenge);
        q["response_type"] = responseType;
        var a = await oauth.AuthorizeAsync(cookie, q);
        // Never a token in a redirect; the error names the unsupported type.
        (a.Location ?? "").Should().NotContain("access_token").And.NotContain("id_token=");
        (a.Location + a.Body).Should().Contain("unsupported_response_type");
    }

    [Fact]
    public async Task Password_grant_is_refused()
    {
        await oauth.EnsureClientsAsync();
        var t = await oauth.TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["username"] = "admin@bluemail.test", ["password"] = StackFixture.UserPassword, ["scope"] = "openid",
        }, ("t-conf", OAuthHelper.Secret));
        t.Error.Should().Be("unsupported_grant_type");
        t.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task Missing_pkce_is_refused_even_for_a_confidential_client()
    {
        var cookie = await CookieAsync();
        foreach (var client in new[] { "t-spa", "t-conf" })
        {
            var a = await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest(client, "openid", null, challenge: null));
            OAuthHelper.ErrorOf(a).Should().Be("invalid_request", client);
        }
    }

    [Fact]
    public async Task Plain_pkce_is_refused()
    {
        var cookie = await CookieAsync();
        var a = await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest("t-spa", "openid", null, "plain-challenge-value-0123456789012345678901", method: "plain"));
        OAuthHelper.ErrorOf(a).Should().Be("invalid_request");
    }

    [Theory]
    [InlineData("http://app.test/cb/")]
    [InlineData("http://app.test/cB")]
    [InlineData("http://app.test/cb?x=1")]
    [InlineData("https://app.test/cb")]
    [InlineData("http://app.test.evil.example/cb")]
    public async Task Redirect_uri_one_character_off_never_redirects(string redirect)
    {
        var cookie = await CookieAsync();
        var (_, challenge) = OAuthHelper.Pkce();
        var a = await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest("t-spa", "openid", null, challenge, redirect: redirect));
        a.Status.Should().Be(HttpStatusCode.BadRequest);
        a.Location.Should().BeNull();
    }

    [Fact]
    public async Task Code_reuse_is_refused_and_revokes_what_it_gave()
    {
        var cookie = await CookieAsync();
        var (verifier, challenge) = OAuthHelper.Pkce();
        var a = await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest("t-spa", "openid offline_access t.read", OAuthHelper.Api, challenge));
        var code = OAuthHelper.QueryOf(a.Location!)["code"];
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = OAuthHelper.Redirect, ["client_id"] = "t-spa", ["code_verifier"] = verifier,
        };
        var first = await oauth.TokenAsync(form);
        first.Status.Should().Be(HttpStatusCode.OK);
        var second = await oauth.TokenAsync(form);
        second.Error.Should().Be("invalid_grant");
        (await oauth.IntrospectAsync(first.RefreshToken!)).GetProperty("active").GetBoolean().Should().BeFalse("reuse of a code revokes the tokens it issued");
    }

    [Fact]
    public async Task Wrong_code_verifier_is_refused()
    {
        var cookie = await CookieAsync();
        var (_, challenge) = OAuthHelper.Pkce();
        var a = await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest("t-spa", "openid", null, challenge));
        var t = await oauth.TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = OAuthHelper.QueryOf(a.Location!)["code"], ["redirect_uri"] = OAuthHelper.Redirect,
            ["client_id"] = "t-spa", ["code_verifier"] = OAuthHelper.Pkce().Verifier,
        });
        t.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Refresh_token_reuse_revokes_the_whole_chain()
    {
        var cookie = await CookieAsync();
        var t0 = await oauth.SignInAndGetTokensAsync(cookie);
        var t1 = await oauth.RefreshAsync(t0.RefreshToken!);
        t1.Status.Should().Be(HttpStatusCode.OK);
        t1.RefreshToken.Should().NotBe(t0.RefreshToken, "refresh tokens rotate");
        (await oauth.RefreshAsync(t0.RefreshToken!)).Error.Should().Be("invalid_grant", "the old token was already used");
        (await oauth.RefreshAsync(t1.RefreshToken!)).Error.Should().Be("invalid_grant", "reuse revoked the newer token of the chain too");
    }

    [Fact]
    public async Task Parallel_refresh_with_one_token_has_exactly_one_winner()
    {
        var cookie = await CookieAsync();
        var t0 = await oauth.SignInAndGetTokensAsync(cookie);
        using var gate = new Barrier(3);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await oauth.RefreshAsync(t0.RefreshToken!);
        })));
        results.Count(r => r.Status == HttpStatusCode.OK).Should().Be(1);
    }

    [Fact]
    public async Task Resource_rules_of_the_contract()
    {
        var cookie = await CookieAsync();
        var (_, ch) = OAuthHelper.Pkce();
        async Task<string> ErrorOf(string scope, string? resource, string client = "t-spa")
        {
            return OAuthHelper.ErrorOf(await oauth.AuthorizeAsync(cookie, OAuthHelper.CodeRequest(client, scope, resource, ch)));
        }

        (await ErrorOf("openid t.read", null)).Should().Be("invalid_target", "resource scopes need the resource parameter");
        (await ErrorOf("openid o.read", OAuthHelper.Api)).Should().Be("invalid_scope", "o.read belongs to another resource");
        (await ErrorOf("openid t.read", "https://unknown.example")).Should().BeOneOf("invalid_target", "invalid_request");
        (await ErrorOf("openid nope.read", OAuthHelper.Api)).Should().Be("invalid_scope");
        // t-third may use Api only: Api2 is not in its permissions.
        (await ErrorOf("openid", OAuthHelper.Api2, "t-third")).Should().BeOneOf("invalid_target", "invalid_request", "unauthorized_client");
        var two = OAuthHelper.CodeRequest("t-spa", "openid", null, ch);
        var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/connect/authorize", two) + "&resource=" + Uri.EscapeDataString(OAuthHelper.Api) +
                  "&resource=" + Uri.EscapeDataString(OAuthHelper.Api2);
        using var c = stack.Web.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", cookie);
        var res = await c.SendAsync(req);
        OAuthHelper.QueryOf(res.Headers.Location!.ToString())["error"].Should().Be("invalid_target", "one resource per token");
    }

    [Fact]
    public async Task Client_secret_post_and_wrong_secrets_are_refused()
    {
        await oauth.EnsureClientsAsync();
        var post = await oauth.TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "t-conf", ["client_secret"] = OAuthHelper.Secret, ["scope"] = "t.read", ["resource"] = OAuthHelper.Api,
        });
        post.Error.Should().Be("invalid_client");
        var wrong = await oauth.TokenAsync(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "t.read", ["resource"] = OAuthHelper.Api },
            ("t-conf", "wrong-secret"));
        wrong.Error.Should().Be("invalid_client");
    }

    [Fact]
    public async Task Private_key_jwt_works_and_a_foreign_key_does_not()
    {
        string Assertion(RSA key, string kid) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "sample-product", Audience = StackFixture.Issuer, Expires = DateTime.UtcNow.AddMinutes(2), TokenType = "client-authentication+jwt",
            Claims = new Dictionary<string, object> { ["sub"] = "sample-product", ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
        });
        Dictionary<string, string> Form(string assertion) => new()
        {
            ["grant_type"] = "client_credentials", ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion, ["scope"] = "sample.read", ["resource"] = StackFixture.SampleApi, ["client_id"] = "sample-product",
        };
        var ok = await oauth.TokenAsync(Form(Assertion(stack.SampleKey, "sample-test")));
        ok.Status.Should().Be(HttpStatusCode.OK, ok.Body.ToString());
        using var other = RSA.Create(2048);
        (await oauth.TokenAsync(Form(Assertion(other, "sample-test")))).Error.Should().Be("invalid_client");
        var replay = Assertion(stack.SampleKey, "sample-test");
        (await oauth.TokenAsync(Form(replay))).Status.Should().Be(HttpStatusCode.OK);
        (await oauth.TokenAsync(Form(replay))).Error.Should().Be("invalid_client", "a client assertion is good once (jti)");
    }

    [Fact]
    public async Task Product_api_accepts_only_its_own_good_tokens()
    {
        var cookie = await CookieAsync();
        // A real token for the sample API (client credentials of the sample itself).
        string assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "sample-product", Audience = StackFixture.Issuer, Expires = DateTime.UtcNow.AddMinutes(2), TokenType = "client-authentication+jwt",
            Claims = new Dictionary<string, object> { ["sub"] = "sample-product", ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(stack.SampleKey) { KeyId = "sample-test" }, SecurityAlgorithms.RsaSha256),
        });
        var good = await oauth.TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion, ["scope"] = "sample.read", ["resource"] = StackFixture.SampleApi, ["client_id"] = "sample-product",
        });
        (await CallSampleApi(good.AccessToken)).Should().Be(HttpStatusCode.OK);

        // Token for another resource (wrong aud).
        var other = await oauth.SignInAndGetTokensAsync(cookie);
        (await CallSampleApi(other.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);

        var active = stack.Web.Services.GetRequiredService<KeyRing>().Active;
        string Forge(SecurityKey key, string alg, DateTime expires, string typ = "at+jwt") => new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = StackFixture.Issuer, Audience = StackFixture.SampleApi, IssuedAt = expires.AddMinutes(-15), NotBefore = expires.AddMinutes(-15), Expires = expires,
                TokenType = typ, Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["idtyp"] = "app" },
                SigningCredentials = new SigningCredentials(key, alg),
            });
        // Signed by BlueID's real key but expired.
        (await CallSampleApi(Forge(active.Key, SecurityAlgorithms.RsaSha256, DateTime.UtcNow.AddMinutes(-5)))).Should().Be(HttpStatusCode.Unauthorized);
        // Same key, right claims, but not an access token type (an id token presented as access token).
        (await CallSampleApi(Forge(active.Key, SecurityAlgorithms.RsaSha256, DateTime.UtcNow.AddMinutes(10), "JWT"))).Should().Be(HttpStatusCode.Unauthorized);
        // Unknown key with the active kid.
        using var stranger = RSA.Create(3072);
        (await CallSampleApi(Forge(new RsaSecurityKey(stranger) { KeyId = active.Kid }, SecurityAlgorithms.RsaSha256, DateTime.UtcNow.AddMinutes(10))))
            .Should().Be(HttpStatusCode.Unauthorized);
        // alg none.
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"at+jwt"}""");
        var payload = Base64UrlEncoder.Encode($$"""{"iss":"{{StackFixture.Issuer}}","aud":"{{StackFixture.SampleApi}}","sub":"x","exp":{{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}}}""");
        (await CallSampleApi(header + "." + payload + ".")).Should().Be(HttpStatusCode.Unauthorized);
        // RS256 -> HS256 swap: HMAC keyed with the public key bytes.
        var publicBytes = Encoding.UTF8.GetBytes(((RsaSecurityKey)active.Key).Rsa.ExportSubjectPublicKeyInfoPem());
        (await CallSampleApi(Forge(new SymmetricSecurityKey(publicBytes) { KeyId = active.Kid }, SecurityAlgorithms.HmacSha256, DateTime.UtcNow.AddMinutes(10))))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpStatusCode> CallSampleApi(string? token)
    {
        using var c = stack.Sample.CreateClient();
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return (await c.GetAsync("/api/whoami")).StatusCode;
    }
}
