using System.Net;
using System.Threading.RateLimiting;
using BlueID.Application;
using BlueID.Application.OAuth;
using BlueID.Application.Seeding;
using BlueID.Application.Web;
using BlueID.Infrastructure;
using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Localization;
using BlueID.Infrastructure.Persistence;
using BlueID.Web;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;
using Serilog;

InfrastructureServices.UseInvariantProcessCulture();

var builder = WebApplication.CreateBuilder(args);
var settings = BidSettings.Load(builder.Configuration);
var testing = builder.Environment.IsEnvironment("Testing");
// Plain HTTP only for local development and tests; production is HTTPS end to end.
var plainHttpAllowed = testing || builder.Environment.IsDevelopment();

builder.AddBlueIdLogging(settings, "web");
builder.Services.AddBlueIdInfrastructure(settings);
builder.Services.AddBlueIdApplication();
builder.Services.AddBlueIdOAuthCore(builder.Configuration);
var conformanceMode = string.Equals(builder.Configuration["BID_CONFORMANCE_MODE"], "true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddBlueIdOAuthServer(settings, plainHttpAllowed, conformanceMode,
    builder.Environment.IsDevelopment() ? builder.Configuration["BID_DEV_HOSTS"] : null);
builder.Services.AddControllers();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddSingleton<IStringLocalizer, JsonStringLocalizer>();
builder.Services.AddBlueIdSessionCookie(plainHttpAllowed, isApi: false);
builder.Services.AddSingleton<PendingPasswordChange>();
// Razor would otherwise write every non-ASCII letter as &#x...; (BUG-003): Vietnamese pages become unreadable source
// and three times larger. HTML-significant characters are still encoded.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddRazorPages(o =>
{
    // Pages are anonymous by nature (sign-in); each page that needs a session says so with [Authorize]-like checks in code.
    o.Conventions.AllowAnonymousToFolder("/");
}).AddViewLocalization();
builder.Services.AddHealthChecks().AddCheck<BlueID.Infrastructure.Health.DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // The client IP comes only from trusted proxies (PROMPT 7).
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var cidr in settings.TrustedProxies)
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(WebRateLimits.SignIn, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        // Per client IP, generous: whole offices sign in from one NAT address (BUG-012). Guessing is stopped by the
        // per-account soft lockout (docs/00 D12), not by this.
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = testing ? 10_000 : int.TryParse(builder.Configuration["BID_SIGNIN_IP_LIMIT_PER_MINUTE"], out var perMinute) ? perMinute : 300,
            Window = TimeSpan.FromMinutes(1),
        }));
    o.OnRejected = (ctx, _) => new ValueTask(ApiResponses.WriteErrorAsync(ctx.HttpContext, HttpStatusCode.TooManyRequests, "rate_limited"));
});

var app = builder.Build();

await StartupTasks.RunAsync(app.Services, builder.Configuration);

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>(
    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
    "form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'");
app.UseMiddleware<InputFilterMiddleware>();
// An OAuth error OpenIddict could not send back to the client (status code pages integration): show the person a
// translated page with the error code instead of an empty 400.
app.UseStatusCodePages(ctx =>
{
    // Only the browser-facing endpoints: APIs (token, userinfo, introspection…) keep their 4xx answers (BUG-015).
    var endpoint = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerEndpointType(ctx.HttpContext);
    if (endpoint is OpenIddict.Server.OpenIddictServerEndpointType.Authorization or OpenIddict.Server.OpenIddictServerEndpointType.EndSession &&
        Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerResponse(ctx.HttpContext) is { Error: { Length: > 0 } error })
    {
        ctx.HttpContext.Response.Redirect("/oauth-error?error=" + Uri.EscapeDataString(error));
    }

    return Task.CompletedTask;
});
app.UseStaticFiles();
var portalRoot = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "portal");
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture(JsonStringLocalizerFactory.Fallback);
    var cultures = JsonStringLocalizerFactory.Languages.Select(l => new System.Globalization.CultureInfo(l)).ToArray();
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.FallBackToParentCultures = true;
    // OIDC ui_locales first (PROMPT 0.2), then the language picked on the page (cookie), then Accept-Language.
    o.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider { QueryStringKey = "ui_locales", UIQueryStringKey = "ui_locales" });
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantFromSessionMiddleware>();
app.UseAuthorization();

app.MapGet("/", (HttpContext http) => Results.Redirect(http.User.Identity?.IsAuthenticated == true ? "/portal/" : "/login")).AllowAnonymous();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapRazorPages();
app.MapControllers();
// The account / admin SPA (web/, built into wwwroot/portal). Client-side routes get its index.html with a fresh
// style nonce (BUG-005); the SPA asks /me/v1 itself and is sent to /login on 401.
if (File.Exists(Path.Combine(portalRoot, "index.html")))
{
    var portalIndex = File.ReadAllText(Path.Combine(portalRoot, "index.html"));
    app.MapGet("/portal/{*path:nonfile}", (HttpContext http) =>
    {
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        http.Items[SecurityHeadersMiddleware.StyleNonceKey] = nonce;
        http.Response.Headers.CacheControl = "no-store";
        return Results.Content(portalIndex.Replace("<head>", $"<head><meta name=\"csp-nonce\" content=\"{nonce}\" />", StringComparison.Ordinal),
            "text/html; charset=utf-8");
    }).AllowAnonymous();
}

app.Run();

namespace BlueID.Web
{
    public static class WebRateLimits
    {
        public const string SignIn = "sign-in";
    }

    /// <summary>Migrations and seed data, once at start (BID_DB_MIGRATE=false skips migrations, e.g. for extra nodes).</summary>
    internal static class StartupTasks
    {
        public static async Task RunAsync(IServiceProvider services, IConfiguration config)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<BlueIdDbContext>();
            if (!string.Equals(config["BID_DB_MIGRATE"], "false", StringComparison.OrdinalIgnoreCase))
            {
                await db.Database.MigrateAsync();
            }

            var seeder = sp.GetRequiredService<SampleDataSeeder>();
            var options = new SeedOptions(config["BID_PLATFORM_DOMAIN"] ?? "blue.test", config["BID_PLATFORM_ADMIN_UPN"], config["BID_PLATFORM_ADMIN_PASSWORD"],
                config["BID_SEED_USER_PASSWORD"]);
            await sp.GetRequiredService<BlueID.Infrastructure.Security.KeyRing>().EnsureAsync(CancellationToken.None);
            await seeder.SeedPlatformAsync(options, CancellationToken.None);
            if (string.Equals(config["BID_SEED_SAMPLE_DATA"], "true", StringComparison.OrdinalIgnoreCase))
            {
                await seeder.SeedSampleTenantsAsync(options, CancellationToken.None);
            }

            await sp.GetRequiredService<BlueID.Application.Seeding.OAuthSeeder>().SeedAsync(CancellationToken.None);
            await sp.GetRequiredService<ApplicationRegistry>().EnforceConformanceModeAsync(CancellationToken.None);
        }
    }
}
