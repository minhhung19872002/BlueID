using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BlueID.IntegrationTests;

/// <summary>
/// Real PostgreSQL 16 (Testcontainers), BlueID.Web (migrates, seeds) and BlueID.Api on test servers, sharing the
/// database and the Data Protection key ring exactly like the deployment.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    public const string PlatformAdmin = "admin@platform.test";
    public const string PlatformAdminPassword = "Platform-Admin-2026!";
    public const string UserPassword = "Sample-User-Pass-2026";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16.16-alpine")
        .WithDatabase("blueid").WithUsername("blueid").WithPassword("blueid-test").Build();

    public WebApplicationFactory<BlueID.Web.PendingPasswordChange> Web { get; private set; } = null!;
    public WebApplicationFactory<BlueID.Api.UserCultureProvider> Api { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        ConnectionString = _pg.GetConnectionString() + ";Include Error Detail=true";
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy", "postgres", "init", "01-extensions.sql")), conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var env = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["BID_DB_CONNECTION"] = ConnectionString,
            ["BID_ISSUER"] = "http://id.test",
            ["BID_PLATFORM_DOMAIN"] = "platform.test",
            ["BID_PLATFORM_ADMIN_UPN"] = PlatformAdmin,
            ["BID_PLATFORM_ADMIN_PASSWORD"] = PlatformAdminPassword,
            ["BID_SEED_SAMPLE_DATA"] = "true",
            ["BID_SEED_USER_PASSWORD"] = UserPassword,
        };
        foreach (var (k, v) in env)
        {
            Environment.SetEnvironmentVariable(k, v);
        }

        Web = new WebApplicationFactory<BlueID.Web.PendingPasswordChange>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        Api = new WebApplicationFactory<BlueID.Api.UserCultureProvider>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        _ = Web.Server; // start: migrate + seed
        _ = Api.Server;
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await Web.DisposeAsync();
        await _pg.DisposeAsync();
    }

    public AsyncServiceScope WebScope() => Web.Services.CreateAsyncScope();

    /// <summary>Signs in through the Razor pages (anti-forgery and two steps) and returns the session cookie header.</summary>
    public async Task<(HttpStatusCode Status, string? Location, string? Cookie, string Body)> SignInAsync(string user, string password)
    {
        using var c = Web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await c.GetStringAsync("/login?ui_locales=en");
        var step2 = await c.PostAsync("/login?handler=Name&ui_locales=en", Form(("UserName", user), ("__RequestVerificationToken", Token(page))));
        var html = await step2.Content.ReadAsStringAsync();
        var res = await c.PostAsync("/login?handler=Password&ui_locales=en",
            Form(("UserName", user), ("Password", password), ("__RequestVerificationToken", Token(html))));
        var cookie = res.Headers.TryGetValues("Set-Cookie", out var sc) ? sc.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("bid_session=", StringComparison.Ordinal)) : null;
        return (res.StatusCode, res.Headers.Location?.ToString(), cookie, await res.Content.ReadAsStringAsync());
    }

    public async Task<string> CookieOfAsync(string user, string password = UserPassword)
    {
        var r = await SignInAsync(user, password);
        return r.Cookie ?? throw new InvalidOperationException($"sign-in of {user} failed: {r.Status}");
    }

    /// <summary>Calls the API as the browser would (session cookie + CSRF header for writes).</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> ApiAsync(string cookie, HttpMethod method, string path, object? body = null, bool csrf = true)
    {
        using var c = Api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Add("Cookie", cookie);
        if (csrf)
        {
            req.Headers.Add("X-BlueID-CSRF", "1");
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static FormUrlEncodedContent Form(params (string, string)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    public static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "BlueID.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<StackFixture>
{
    public const string Name = "stack";
}
