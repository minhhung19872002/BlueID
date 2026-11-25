using System.Net;
using System.Reflection;
using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Web;
using BlueID.Domain;
using BlueID.Infrastructure.Audit;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace BlueID.IntegrationTests;

[Collection(StackCollection.Name)]
public sealed class Phase1Tests(StackFixture stack)
{
    [Fact]
    public async Task Password_sign_in_gives_a_session_the_api_accepts()
    {
        var r = await stack.SignInAsync("admin@acme.test", StackFixture.UserPassword);
        r.Status.Should().Be(HttpStatusCode.Redirect);
        r.Location.Should().Be("/portal/");
        r.Cookie.Should().NotBeNull();
        var (status, me) = await stack.ApiAsync(r.Cookie!, HttpMethod.Get, "/me/v1");
        status.Should().Be(HttpStatusCode.OK);
        me.GetProperty("success").GetBoolean().Should().BeTrue();
        me.GetProperty("data").GetProperty("user").GetProperty("userPrincipalName").GetString().Should().Be("admin@acme.test");
        me.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain("directory.users.write");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_answer()
    {
        var wrong = await stack.SignInAsync("admin@acme.test", "not-the-password-1");
        var unknown = await stack.SignInAsync("nobody-here@acme.test", "not-the-password-1");
        foreach (var r in new[] { wrong, unknown })
        {
            r.Status.Should().Be(HttpStatusCode.OK);
            r.Cookie.Should().BeNull();
            r.Body.Should().Contain("The user name or password is incorrect.");
        }

        // The page body is identical except for the anti-forgery token.
        static string Strip(string html) => System.Text.RegularExpressions.Regex.Replace(html, "value=\"CfDJ8[^\"]+\"", "");
        Strip(wrong.Body).Replace("admin@acme.test", "X", StringComparison.Ordinal)
            .Should().Be(Strip(unknown.Body).Replace("nobody-here@acme.test", "X", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_user_cannot_sign_in_and_loses_open_sessions_at_once()
    {
        var upn = "disable-me@acme.test";
        await using (var scope = stack.WebScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == "acme");
            await scope.ServiceProvider.GetRequiredService<IDirectoryWriter>()
                .CreateUserAsync(tenant.Id, new NewUser(upn, "Disable Me", Password: StackFixture.UserPassword), Actor.System, default);
        }

        var cookie = await stack.CookieOfAsync(upn);
        (await stack.ApiAsync(cookie, HttpMethod.Get, "/me/v1")).Status.Should().Be(HttpStatusCode.OK);
        var admin = await stack.CookieOfAsync("admin@acme.test");
        var (_, list) = await stack.ApiAsync(admin, HttpMethod.Get, "/admin/v1/users?search=disable-me");
        var id = list.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetString();
        (await stack.ApiAsync(admin, HttpMethod.Post, $"/admin/v1/users/{id}/disable")).Status.Should().Be(HttpStatusCode.OK);

        (await stack.ApiAsync(cookie, HttpMethod.Get, "/me/v1")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await stack.SignInAsync(upn, StackFixture.UserPassword)).Cookie.Should().BeNull();
        await using var check = stack.WebScope();
        var events = await check.ServiceProvider.GetRequiredService<BlueIdDbContext>().Outbox
            .Where(e => e.SubjectId == Guid.Parse(id!)).OrderBy(e => e.SubjectSeq).Select(e => new { e.EventType, e.SubjectSeq }).ToListAsync();
        events.Select(e => e.EventType).Should().Equal("user.created", "user.disabled");
        events.Select(e => e.SubjectSeq).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Another_tenant_is_invisible_on_every_admin_endpoint()
    {
        var acme = await stack.CookieOfAsync("admin@acme.test");
        var bluemail = await stack.CookieOfAsync("admin@bluemail.test");
        var (_, other) = await stack.ApiAsync(bluemail, HttpMethod.Get, "/admin/v1/users?top=1");
        var foreignId = other.GetProperty("data").GetProperty("items")[0].GetProperty("id").GetString();

        (await stack.ApiAsync(acme, HttpMethod.Get, $"/admin/v1/users/{foreignId}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await stack.ApiAsync(acme, HttpMethod.Patch, $"/admin/v1/users/{foreignId}", new { displayName = "Hijacked" })).Status.Should().Be(HttpStatusCode.NotFound);
        (await stack.ApiAsync(acme, HttpMethod.Post, $"/admin/v1/users/{foreignId}/disable")).Status.Should().Be(HttpStatusCode.NotFound);
        (await stack.ApiAsync(acme, HttpMethod.Post, $"/admin/v1/users/{foreignId}/revoke-sessions")).Status.Should().Be(HttpStatusCode.NotFound);
        var (_, page) = await stack.ApiAsync(acme, HttpMethod.Get, "/admin/v1/users?top=999&count=true");
        page.GetProperty("data").GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetString()).Should().NotContain(foreignId);
        // Platform endpoints: a customer's global administrator is not a platform operator.
        (await stack.ApiAsync(acme, HttpMethod.Get, "/admin/v1/platform/tenants")).Status.Should().Be(HttpStatusCode.Forbidden);
        // Creating a user in a domain of another tenant is refused.
        var (st, body) = await stack.ApiAsync(acme, HttpMethod.Post, "/admin/v1/users", new
        {
            userPrincipalName = "intruder@bluemail.test", displayName = "Intruder", password = "Intruder-Pass-2026", mustChangePassword = false,
        });
        st.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("domain_not_verified");
    }

    [Fact]
    public async Task User_without_admin_role_is_forbidden_and_writes_need_the_csrf_header()
    {
        await using (var scope = stack.WebScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == "acme");
            await scope.ServiceProvider.GetRequiredService<IDirectoryWriter>()
                .CreateUserAsync(tenant.Id, new NewUser("plain.user@acme.test", "Plain User", Password: StackFixture.UserPassword), Actor.System, default);
        }

        var plain = await stack.CookieOfAsync("plain.user@acme.test");
        (await stack.ApiAsync(plain, HttpMethod.Get, "/admin/v1/users")).Status.Should().Be(HttpStatusCode.Forbidden);
        (await stack.ApiAsync(plain, HttpMethod.Patch, "/me/v1/settings", new { hourCycle = "h12" }, csrf: false)).Status.Should().Be(HttpStatusCode.Forbidden);
        var (ok, me) = await stack.ApiAsync(plain, HttpMethod.Patch, "/me/v1/settings", new { hourCycle = "h12", timeZone = "Asia/Ho_Chi_Minh" });
        ok.Should().Be(HttpStatusCode.OK);
        me.GetProperty("data").GetProperty("hourCycle").GetString().Should().Be("h12");
        var (bad, err) = await stack.ApiAsync(plain, HttpMethod.Patch, "/me/v1/settings", new { timeZone = "Mars/Olympus" });
        bad.Should().Be(HttpStatusCode.BadRequest);
        err.GetProperty("errors")[0].GetProperty("field").GetString().Should().Be("timeZone");
    }

    [Fact]
    public async Task Three_truly_parallel_creations_of_one_sign_in_name_give_exactly_one_user()
    {
        Guid tenantId;
        await using (var scope = stack.WebScope())
        {
            tenantId = (await scope.ServiceProvider.GetRequiredService<BlueIdDbContext>().Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == "acme")).Id;
        }

        using var gate = new Barrier(3);
        var tasks = Enumerable.Range(0, 3).Select(i => Task.Run(async () =>
        {
            await using var scope = stack.WebScope();
            var writer = scope.ServiceProvider.GetRequiredService<IDirectoryWriter>();
            gate.SignalAndWait();
            try
            {
                await writer.CreateUserAsync(tenantId, new NewUser("Race.Winner@ACME.test", $"Racer {i}"), Actor.System, default);
                return "created";
            }
            catch (AppException ex)
            {
                return ex.Code;
            }
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        results.Count(r => r == "created").Should().Be(1);
        results.Where(r => r != "created").Should().OnlyContain(r => r == "upn_taken" || r == "address_taken");
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted()
    {
        await using (var scope = stack.WebScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == "bluemail");
            await scope.ServiceProvider.GetRequiredService<IDirectoryWriter>()
                .CreateUserAsync(tenant.Id, new NewUser("guess.target@bluemail.test", "Guess Target", Password: StackFixture.UserPassword), Actor.System, default);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var scope = stack.WebScope();
            await scope.ServiceProvider.GetRequiredService<Application.Auth.SignInService>()
                .PasswordSignInAsync(new Application.Auth.SignInRequest("guess.target@bluemail.test", "wrong-guess-12345", "198.51.100.7", "test"), default);
        })));
        await using var check = stack.WebScope();
        var db2 = check.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var row = await db2.Lockouts.SingleAsync(l => l.Key.EndsWith("|ip:198.51.100.7"));
        row.Failures.Should().Be(6);
        row.Until.Should().NotBeNull();
        // Locked: even the right password is refused from that IP for now, with the same generic answer...
        var locked = await check.ServiceProvider.GetRequiredService<Application.Auth.SignInService>()
            .PasswordSignInAsync(new Application.Auth.SignInRequest("guess.target@bluemail.test", StackFixture.UserPassword, "198.51.100.7", "test"), default);
        locked.Status.Should().Be(Application.Auth.SignInStatus.Failed);
        // ...but the owner signing in from another address is not locked out by the attacker.
        var owner = await check.ServiceProvider.GetRequiredService<Application.Auth.SignInService>()
            .PasswordSignInAsync(new Application.Auth.SignInRequest("guess.target@bluemail.test", StackFixture.UserPassword, "203.0.113.20", "test"), default);
        owner.Status.Should().Be(Application.Auth.SignInStatus.Success);
    }

    [Fact]
    public async Task Required_password_change_gives_no_session_until_done()
    {
        var r = await stack.SignInAsync(StackFixture.PlatformAdmin, StackFixture.PlatformAdminPassword);
        r.Status.Should().Be(HttpStatusCode.Redirect);
        r.Location.Should().StartWith("/password/change?t=");
        r.Cookie.Should().BeNull();
    }

    [Fact]
    public async Task Audit_chain_verifies_and_every_change_has_its_change_log_row()
    {
        await using var scope = stack.WebScope();
        var sp = scope.ServiceProvider;
        (await sp.GetRequiredService<AuditChainVerifier>().VerifyAsync()).Valid.Should().BeTrue();
        var db = sp.GetRequiredService<BlueIdDbContext>();
        var users = await db.Users.IgnoreQueryFilters().CountAsync();
        var created = await db.ChangeLog.IgnoreQueryFilters().CountAsync(c => c.ObjectType == "user" && c.Change == "created");
        created.Should().Be(users);
        // change_seq has no duplicates within a tenant (unique index) and starts at 1.
        var firsts = await db.ChangeLog.IgnoreQueryFilters().GroupBy(c => c.TenantId).Select(g => g.Min(c => c.ChangeSeq)).ToListAsync();
        firsts.Should().OnlyContain(s => s == 1);
    }

    [Fact]
    public async Task Tampering_with_an_audit_row_is_detected()
    {
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var id = await db.AuditLogs.OrderBy(a => a.Id).Select(a => a.Id).Skip(2).FirstAsync();
        var original = await db.AuditLogs.Where(a => a.Id == id).Select(a => a.Action).FirstAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE audit.audit_logs SET action = 'forged' WHERE id = {id}");
        try
        {
            var v = await scope.ServiceProvider.GetRequiredService<AuditChainVerifier>().VerifyAsync();
            v.Valid.Should().BeFalse();
            v.FirstBrokenId.Should().Be(id);
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"UPDATE audit.audit_logs SET action = {original} WHERE id = {id}");
        }
    }

    [Fact]
    public async Task Nul_characters_are_refused_at_the_door()
    {
        var cookie = await stack.CookieOfAsync("admin@acme.test");
        (await stack.ApiAsync(cookie, HttpMethod.Get, "/admin/v1/users?search=a%00b")).Status.Should().Be(HttpStatusCode.BadRequest);
        var (st, me) = await stack.ApiAsync(cookie, HttpMethod.Get, "/me/v1");
        st.Should().Be(HttpStatusCode.OK);
        var id = me.GetProperty("data").GetProperty("user").GetProperty("id").GetString();
        var (ok, u) = await stack.ApiAsync(cookie, HttpMethod.Patch, $"/admin/v1/users/{id}", new { jobTitle = "Chief\0Admin" });
        ok.Should().Be(HttpStatusCode.OK);
        u.GetProperty("data").GetProperty("jobTitle").GetString().Should().Be("ChiefAdmin");
    }

    [Fact]
    public void Every_api_endpoint_states_its_guard()
    {
        // Lesson 3: [Authorize] is not a guard. Every action says which permission it needs, or why it is self-guarded.
        var actions = typeof(BlueID.Api.UserCultureProvider).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(m => (t, m)))
            .ToList();
        actions.Should().NotBeEmpty();
        var unguarded = actions.Where(x =>
            x.m.GetCustomAttribute<RequirePermissionAttribute>() is null && x.t.GetCustomAttribute<RequirePermissionAttribute>() is null &&
            x.m.GetCustomAttribute<SelfGuardedAttribute>() is null && x.t.GetCustomAttribute<SelfGuardedAttribute>() is null &&
            x.m.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Select(x => $"{x.t.Name}.{x.m.Name}").ToList();
        unguarded.Should().BeEmpty();
    }

    [Fact]
    public void Every_migration_class_has_the_migration_attribute()
    {
        var missing = typeof(BlueIdDbContext).Assembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttribute<MigrationAttribute>() is null)
            .Select(t => t.Name).ToList();
        missing.Should().BeEmpty();
    }

    [Fact]
    public async Task The_model_has_no_pending_changes_without_a_migration()
    {
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        db.Database.HasPendingModelChanges().Should().BeFalse();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Tenant_filter_is_closed_when_no_tenant_is_set()
    {
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().BeGreaterThan(80);
    }

    [Fact]
    public async Task Pages_send_strict_security_headers_and_answer_in_vietnamese()
    {
        using var c = stack.Web.CreateClient();
        var res = await c.GetAsync("/login?ui_locales=vi");
        res.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'").And.Contain("frame-ancestors 'none'");
        res.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
        (await res.Content.ReadAsStringAsync()).Should().Contain("Đăng nhập").And.Contain("lang=\"vi\"");
        (await c.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Open_redirects_from_the_sign_in_page_are_refused()
    {
        using var c = stack.Web.CreateClient(new WebApplicationFactoryClientOptionsNoRedirect());
        var page = await c.GetStringAsync("/login?ReturnUrl=https://evil.example/");
        page.Should().NotContain("evil.example\"", "the return URL is not echoed into a link target");
        var r = await stack.SignInAsync("admin@bluemail.test", StackFixture.UserPassword);
        r.Location.Should().Be("/portal/");
    }

    private sealed class WebApplicationFactoryClientOptionsNoRedirect : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        public WebApplicationFactoryClientOptionsNoRedirect() => AllowAutoRedirect = false;
    }
}

[Collection(StackCollection.Name)]
public sealed class Phase1PortalTests(StackFixture stack)
{
    [Fact]
    public async Task Portal_index_carries_a_fresh_style_nonce_matching_its_csp()
    {
        File.Exists(Path.Combine(StackFixture.RepoRoot(), "src", "BlueID.Web", "wwwroot", "portal", "index.html"))
            .Should().BeTrue("build the portal first: cd web && npm run build");
        using var c = stack.Web.CreateClient();
        var a = await c.GetAsync("/portal/admin/users");
        var b = await c.GetAsync("/portal/");
        string Nonce(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"csp-nonce\" content=\"([^\"]+)\"").Groups[1].Value;
        var na = Nonce(await a.Content.ReadAsStringAsync());
        var nb = Nonce(await b.Content.ReadAsStringAsync());
        na.Should().NotBeEmpty().And.NotBe(nb);
        a.Headers.GetValues("Content-Security-Policy").Single().Should().Contain($"style-src 'self' 'nonce-{na}'").And.NotContain("unsafe-inline");
        (await c.GetAsync("/login")).Headers.GetValues("Content-Security-Policy").Single().Should().Contain("style-src 'self';");
    }

    [Fact]
    public async Task Sample_names_keep_their_order()
    {
        await using var scope = stack.WebScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.UserPrincipalName == "admin@acme.test");
        admin.DisplayName.Should().Be("Quản Trị");
    }
}
