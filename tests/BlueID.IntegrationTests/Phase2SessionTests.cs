using System.Net;
using System.Text.Json;
using BlueID.Application.Directory;
using BlueID.Application.OAuth;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.IntegrationTests;

[Collection(StackCollection.Name)]
public sealed class Phase2SessionTests(StackFixture stack)
{
    private readonly OAuthHelper oauth = new(stack);

    private async Task<(Guid TenantId, string Upn)> NewUserAsync(string tenantSlug = "acme")
    {
        await oauth.EnsureClientsAsync();
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == tenantSlug);
        var upn = $"p2-{Guid.NewGuid():N}"[..14] + "@" + (tenantSlug == "acme" ? "acme.test" : "bluemail.test");
        await scope.ServiceProvider.GetRequiredService<IDirectoryWriter>().CreateUserAsync(tenant.Id,
            new NewUser(upn, "Phase Two", Password: StackFixture.UserPassword), Actor.System, default);
        return (tenant.Id, upn);
    }

    [Fact]
    public async Task Third_party_app_asks_for_consent_once()
    {
        var (_, upn) = await NewUserAsync();
        var cookie = await stack.CookieOfAsync(upn);
        var (_, ch) = OAuthHelper.Pkce();
        var req = OAuthHelper.CodeRequest("t-third", "openid profile t.read", OAuthHelper.Api, ch);
        var none = new Dictionary<string, string?>(req) { ["prompt"] = "none" };
        OAuthHelper.ErrorOf(await oauth.AuthorizeAsync(cookie, none)).Should().Be("consent_required");

        var a = await oauth.AuthorizeAsync(cookie, req);
        a.Location.Should().StartWith("/consent?ReturnUrl=");
        using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var get = new HttpRequestMessage(HttpMethod.Get, a.Location);
        get.Headers.Add("Cookie", cookie);
        var page = await c.SendAsync(get);
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("Acme Expense Tracker").And.Contain("Read test data");
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var antiforgeryCookie = page.Headers.GetValues("Set-Cookie").First(v => v.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)).Split(';')[0];
        using var post = new HttpRequestMessage(HttpMethod.Post, a.Location)
        {
            Content = new FormUrlEncodedContent([new("decision", "allow"), new("__RequestVerificationToken", token),
                new("ReturnUrl", Uri.UnescapeDataString(a.Location!["/consent?ReturnUrl=".Length..]))]),
        };
        post.Headers.Add("Cookie", cookie + "; " + antiforgeryCookie);
        var allowed = await c.SendAsync(post);
        allowed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        allowed.Headers.Location!.ToString().Should().StartWith("/connect/authorize?");

        // Consent remembered: no consent page, a code straight away, even with prompt=none.
        OAuthHelper.ErrorOf(await oauth.AuthorizeAsync(cookie, none)).Should().Be("none");
    }

    [Fact]
    public async Task Prompt_none_without_session_and_prompt_login_with_session()
    {
        await oauth.EnsureClientsAsync();
        var (_, ch) = OAuthHelper.Pkce();
        var none = OAuthHelper.CodeRequest("t-spa", "openid", null, ch);
        none["prompt"] = "none";
        OAuthHelper.ErrorOf(await oauth.AuthorizeAsync(null, none)).Should().Be("login_required");

        var cookie = await stack.CookieOfAsync("admin@acme.test");
        var login = OAuthHelper.CodeRequest("t-spa", "openid", null, ch);
        login["prompt"] = "login";
        login["login_hint"] = "admin@acme.test";
        var a = await oauth.AuthorizeAsync(cookie, login);
        a.Location.Should().StartWith("/login?ReturnUrl=").And.Contain("login_hint=admin%40acme.test");
        Uri.UnescapeDataString(a.Location!).Should().NotContain("prompt=login", "no loop after signing in again");
        // Adding a marker by hand does not skip the forced sign-in, nor does replaying the genuine one with the old session.
        var forged = new Dictionary<string, string?>(login) { ["bid_reauth"] = "1" };
        (await oauth.AuthorizeAsync(cookie, forged)).Location.Should().StartWith("/login?");
        var genuine = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://x" + Uri.UnescapeDataString(a.Location!["/login?ReturnUrl=".Length..].Split("&login_hint")[0])).Query)["bid_reauth"].ToString();
        var replay = new Dictionary<string, string?>(login) { ["bid_reauth"] = genuine };
        (await oauth.AuthorizeAsync(cookie, replay)).Location.Should().StartWith("/login?", "the session predates the marker");
        // After signing in again (a new session), the same marker completes the request.
        var fresh = await stack.CookieOfAsync("admin@acme.test");
        OAuthHelper.ErrorOf(await oauth.AuthorizeAsync(fresh, replay)).Should().Be("none");
    }

    [Fact]
    public async Task Sign_out_reaches_the_product_over_the_back_channel()
    {
        var (_, upn) = await NewUserAsync();
        var cookie = await stack.CookieOfAsync(upn);
        // The user signs in to the sample product (its client); BlueID records that the session was used by it.
        var (_, ch) = OAuthHelper.Pkce();
        var a = await oauth.AuthorizeAsync(cookie, new Dictionary<string, string?>
        {
            ["client_id"] = "sample-product", ["response_type"] = "code", ["redirect_uri"] = StackFixture.SampleUrl + "/signin-oidc", ["scope"] = "openid profile",
            ["code_challenge"] = ch, ["code_challenge_method"] = "S256", ["state"] = "s", ["nonce"] = "n",
        });
        OAuthHelper.ErrorOf(a).Should().Be("none");
        var sid = await SessionIdOf(upn);

        // RP-initiated logout at BlueID.
        using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, "/connect/logout");
        req.Headers.Add("Cookie", cookie);
        (await c.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var sender = stack.Web.Services.GetRequiredService<BackchannelLogoutSender>();
        await sender.RunOnceAsync(default);
        await using (var dbg = stack.WebScope())
        {
            var d = await dbg.ServiceProvider.GetRequiredService<BlueIdDbContext>().LogoutDeliveries.SingleOrDefaultAsync(x => x.SessionId == sid);
            var dbx = dbg.ServiceProvider.GetRequiredService<BlueIdDbContext>();
            var clients = await dbx.SessionClients.Where(x => x.SessionId == sid).Select(x => x.ClientId).ToListAsync();
            var sess = await dbx.Sessions.IgnoreQueryFilters().SingleAsync(x => x.Id == sid);
            var app = await dbx.ClientApplications.IgnoreQueryFilters().SingleAsync(x => x.ClientId == "sample-product");
            d.Should().NotBeNull($"clients=[{string.Join(",", clients)}] revoked={sess.RevokedAt} reason={sess.RevokeReason} bcl={app.BackchannelLogoutUri}");
            (d!.LastError).Should().BeNull($"delivery {d.Status} to {d.Uri}");
        }

        stack.Sample.Services.GetRequiredService<SampleProduct.RevokedSessions>().Contains(sid.ToString("D")).Should().BeTrue();
        await using var scope = stack.WebScope();
        var row = await scope.ServiceProvider.GetRequiredService<BlueIdDbContext>().LogoutDeliveries.SingleAsync(d => d.SessionId == sid);
        row.Status.Should().Be(DeliveryStatus.Delivered);
        // The BlueID session itself is over.
        (await stack.ApiAsync(cookie, HttpMethod.Get, "/me/v1")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_redirects_only_with_a_valid_id_token_hint()
    {
        var (_, upn) = await NewUserAsync();
        async Task<string?> LogoutLocation(string? idTokenHint)
        {
            var cookie = await stack.CookieOfAsync(upn);
            using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
            var q = new Dictionary<string, string?> { ["post_logout_redirect_uri"] = "http://app.test/bye", ["state"] = "x", ["id_token_hint"] = idTokenHint };
            using var req = new HttpRequestMessage(HttpMethod.Get, Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/connect/logout", q));
            req.Headers.Add("Cookie", cookie);
            return (await c.SendAsync(req)).Headers.Location?.ToString();
        }

        (await LogoutLocation(null)).Should().Be("/logout", "no hint: nobody vouches for the redirect URI");
        var t = await oauth.SignInAndGetTokensAsync(await stack.CookieOfAsync(upn));
        var tampered = t.IdToken![..^4] + (t.IdToken[^4] == 'A' ? "BBBB" : "AAAA");
        (await LogoutLocation(tampered)).Should().NotStartWith("http://app.test/bye", "a modified hint is no hint");
        (await LogoutLocation(t.IdToken)).Should().StartWith("http://app.test/bye", "valid hint of t-spa, its registered URI");
    }

    [Fact]
    public async Task Refused_or_outdated_logout_deliveries_stop()
    {
        await oauth.EnsureClientsAsync();
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var platform = (await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Type == TenantType.Platform)).Id;
        var reg = scope.ServiceProvider.GetRequiredService<ApplicationRegistry>();
        var def = new ClientDefinition("t-bcl", "BCL test", ApplicationKind.Public, [OAuthHelper.Redirect], [], [GrantTypes.AuthorizationCode], [], ["openid"],
            BackchannelLogoutUri: StackFixture.SampleUrl + "/does-not-exist");
        await reg.EnsureClientAsync(platform, def, default);
        // A delivery the receiver answers with 404 stops at once.
        db.LogoutDeliveries.Add(new LogoutDelivery { SessionId = Guid.NewGuid(), UserId = Guid.NewGuid(), TenantId = platform, ClientId = "t-bcl",
            Uri = StackFixture.SampleUrl + "/does-not-exist", Status = DeliveryStatus.Pending, NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedAt = DateTimeOffset.UtcNow });
        // A delivery still waiting when the URI is removed is cancelled.
        db.LogoutDeliveries.Add(new LogoutDelivery { SessionId = Guid.NewGuid(), UserId = Guid.NewGuid(), TenantId = platform, ClientId = "t-bcl",
            Uri = StackFixture.SampleUrl + "/does-not-exist", Status = DeliveryStatus.Pending, NextAttemptAt = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await stack.Web.Services.GetRequiredService<BackchannelLogoutSender>().RunOnceAsync(default);
        await reg.EnsureClientAsync(platform, def with { BackchannelLogoutUri = null }, default);
        var rows = await db.LogoutDeliveries.AsNoTracking().Where(x => x.ClientId == "t-bcl").ToListAsync();
        rows.Should().OnlyContain(r => r.Status == DeliveryStatus.Failed);
        rows.Select(r => r.LastError).Should().Contain(["refused HTTP 404", "backchannel_logout_uri changed"]);
    }

    [Fact]
    public async Task Disabling_a_user_stops_refresh_and_tells_the_product()
    {
        var (tenantId, upn) = await NewUserAsync();
        var cookie = await stack.CookieOfAsync(upn);
        var t = await oauth.SignInAndGetTokensAsync(cookie);
        var sub = Guid.Parse(OAuthHelper.Read(t.AccessToken!).Subject);
        await using (var scope = stack.WebScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDirectoryWriter>().SetUserEnabledAsync(tenantId, sub, false, "admin_action", Actor.System, default);
        }

        (await oauth.RefreshAsync(t.RefreshToken!)).Error.Should().Be("invalid_grant");
        await using var check = stack.WebScope();
        var db = check.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        (await db.Outbox.AnyAsync(e => e.SubjectId == sub && e.EventType == "user.disabled")).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_stops_after_ninety_days_from_sign_in()
    {
        var (_, upn) = await NewUserAsync();
        var cookie = await stack.CookieOfAsync(upn);
        var t = await oauth.SignInAndGetTokensAsync(cookie);
        var sid = await SessionIdOf(upn);
        await using (var scope = stack.WebScope())
        {
            await scope.ServiceProvider.GetRequiredService<BlueIdDbContext>().Sessions.IgnoreQueryFilters().Where(s => s.Id == sid)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddDays(-91)));
        }

        (await oauth.RefreshAsync(t.RefreshToken!)).Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Key_rotation_reaches_tokens_and_jwks_without_a_restart()
    {
        await oauth.EnsureClientsAsync();
        var ring = stack.Web.Services.GetRequiredService<KeyRing>();
        var before = ring.Active.Kid;
        var next = ring.Keys.Single(k => k.Status == SigningKeyStatus.Next).Kid;
        await using (var scope = stack.WebScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
            // Pretend 90 days passed for ACTIVE and 3 days for NEXT (published long enough).
            await db.SigningKeys.Where(k => k.Kid == before).ExecuteUpdateAsync(k => k.SetProperty(x => x.ActivatedAt, DateTimeOffset.UtcNow.AddDays(-91)));
            await db.SigningKeys.Where(k => k.Kid == next).ExecuteUpdateAsync(k => k.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddDays(-3)));
        }

        (await ring.RotateAsync(default)).Should().BeTrue();
        ring.Active.Kid.Should().Be(next);
        var cookie = await stack.CookieOfAsync("admin@bluemail.test");
        OAuthHelper.Read((await oauth.SignInAndGetTokensAsync(cookie)).AccessToken!).Kid.Should().Be(next, "new tokens use the new key at once");
        using var c = stack.Web.CreateClient();
        var kids = JsonDocument.Parse(await c.GetStringAsync("/.well-known/jwks.json")).RootElement.GetProperty("keys").EnumerateArray()
            .Select(k => k.GetProperty("kid").GetString()).ToList();
        kids.Should().Contain([before, next], "the retiring key stays published for tokens it signed");
        kids.Should().HaveCount(3, "plus the new NEXT key");
    }

    [Fact]
    public async Task Conformance_exemption_cannot_exist_outside_conformance_mode()
    {
        await using var scope = stack.WebScope();
        var reg = scope.ServiceProvider.GetRequiredService<ApplicationRegistry>();
        reg.ConformanceMode.Should().BeFalse("tests run like production");
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var platform = (await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Type == TenantType.Platform)).Id;
        var act = () => reg.EnsureClientAsync(platform, new ClientDefinition("sneaky", "Sneaky", ApplicationKind.Confidential, [OAuthHelper.Redirect], [],
            [GrantTypes.AuthorizationCode], [], ["openid"], ClientSecret: "x-secret-0123456789", ConformanceTestClient: true), default);
        (await act.Should().ThrowAsync<Application.Common.AppException>()).Which.Code.Should().Be("conformance_mode_off");

        // A flagged client copied from a dev database is disabled at start and gets PKCE back.
        db.ClientApplications.Add(new ClientApplication { TenantId = platform, ClientId = "t-spa-copy", Name = "copy", ConformanceTestClient = true, Enabled = true });
        await db.SaveChangesAsync();
        await reg.EnforceConformanceModeAsync(default);
        (await db.ClientApplications.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.ClientId == "t-spa-copy")).Enabled.Should().BeFalse();
    }

    private async Task<Guid> SessionIdOf(string upn)
    {
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.UserPrincipalName == upn);
        return await db.Sessions.IgnoreQueryFilters().Where(s => s.UserId == user.Id && s.RevokedAt == null).OrderByDescending(s => s.CreatedAt).Select(s => s.Id).FirstAsync();
    }
}
