using BlueID.Application.Directory;
using BlueID.Application.OAuth;
using BlueID.Domain;
using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace BlueID.Application.Seeding;

/// <summary>
/// Resources and clients registered at start (PROMPT 6.5): BlueID's own directory resource always; Blue products when
/// their identifiers are configured (BID_RESOURCE_MAIL, BID_RESOURCE_CHAT); the sample product (BID_SAMPLE_URL); the
/// conformance-suite tenant and clients only with BID_CONFORMANCE_MODE=true (docs/00 D14). Idempotent.
/// </summary>
public sealed class OAuthSeeder(BlueIdDbContext db, ApplicationRegistry registry, IDirectoryWriter writer, BidSettings settings, IConfiguration config,
    ILogger<OAuthSeeder> log)
{
    public const string DirectoryResourceSuffix = "/directory";

    public static readonly ResourceScope[] DirectoryScopes =
    [
        new("directory.read", "Read the people and groups of your organisation"),
        new("directory.readwrite", "Read and change the people and groups of your organisation", true),
        new("directory.read.all", "Read the whole directory of the organisation (application)", true),
        new("directory.readwrite.all", "Read and change the whole directory of the organisation (application)", true),
        new("directory.delta", "Follow changes of the directory (application)", true),
        new("internal.app_passwords.verify", "Check app passwords (Blue products only)", true),
        new("internal.users.lookup", "Look up users by address (Blue products only)", true),
        new("internal.sessions.read", "Read the state of sign-in sessions (Blue products only)", true),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var platform = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Type == TenantType.Platform, ct);
        if (platform is null)
        {
            log.LogWarning("No platform tenant yet: resources and clients not seeded");
            return;
        }

        await registry.EnsureResourceAsync(platform.Id, new ResourceDefinition(settings.Issuer + DirectoryResourceSuffix, "BlueID directory", DirectoryScopes), ct);
        if (config["BID_RESOURCE_MAIL"] is { Length: > 0 } mail)
        {
            await registry.EnsureResourceAsync(platform.Id, new ResourceDefinition(mail, "BlueMail",
                [new("mail.read", "Read your mail"), new("mail.send", "Send mail as you"), new("mail.settings", "Change your mail settings")],
                ["mail.standard", "mail.premium"]), ct);
        }

        if (config["BID_RESOURCE_CHAT"] is { Length: > 0 } chat)
        {
            await registry.EnsureResourceAsync(platform.Id, new ResourceDefinition(chat, "BlueChat",
                [new("chat.read", "Read your conversations"), new("chat.write", "Send messages as you")], ["chat.standard"]), ct);
        }

        if (config["BID_SAMPLE_URL"] is { Length: > 0 } sampleUrl && config["BID_SAMPLE_JWKS_FILE"] is { Length: > 0 } jwksFile)
        {
            var sample = sampleUrl.TrimEnd('/');
            var api = sample + "/api";
            await registry.EnsureResourceAsync(platform.Id, new ResourceDefinition(api, "Sample product", [new("sample.read", "Read sample data")]), ct);
            await registry.EnsureClientAsync(platform.Id, new ClientDefinition(
                "sample-product", "Sample product", ApplicationKind.Confidential,
                [sample + "/signin-oidc"], [sample + "/signout-callback-oidc"],
                [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken, GrantTypes.ClientCredentials],
                [api, settings.Issuer + DirectoryResourceSuffix],
                [.. ApplicationRegistry.OidcScopes, "sample.read", "directory.read.all"],
                Jwks: new JsonWebKeySet(await File.ReadAllTextAsync(jwksFile, ct)),
                FirstParty: true, MultiTenant: true, BackchannelLogoutUri: sample + "/blueid/backchannel-logout"), ct);
        }

        if (registry.ConformanceMode)
        {
            await SeedConformanceAsync(ct);
        }
    }

    /// <summary>The tenant, user and clients the OpenID conformance suite signs in with (docs/06 P2, scripts/conformance).</summary>
    private async Task SeedConformanceAsync(CancellationToken ct)
    {
        var password = config["BID_CONFORMANCE_USER_PASSWORD"] ?? throw new InvalidOperationException("BID_CONFORMANCE_USER_PASSWORD is required in conformance mode.");
        var suite = (config["BID_CONFORMANCE_SUITE_URL"] ?? "https://localhost.emobix.co.uk:8443").TrimEnd('/') + "/test/a/blueid";
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == "conformance" && t.DeletedAt == null, ct);
        if (tenant is null)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            tenant = await writer.CreateTenantAsync("OpenID conformance", "conformance", TenantType.Customer, "en", "UTC", Actor.System, ct);
            await writer.AddDomainAsync(tenant.Id, "conformance.test", verified: true, isDefault: true, Actor.System, ct);
            await writer.CreateUserAsync(tenant.Id, new NewUser("oidc.tester@conformance.test", "OIDC Tester", "OIDC", "Tester",
                Locale: "en", TimeZone: "Europe/Paris", Password: password), Actor.System, ct);
            await tx.CommitAsync(ct);
        }

        // The suite's client_secret_post module needs a client registered for that method (docs/00 D16).
        await registry.EnsureClientAsync(tenant.Id, new ClientDefinition(
            "conformance-post", "OpenID conformance client (client_secret_post)", ApplicationKind.Confidential,
            [suite + "/callback"], [suite + "/post_logout_redirect"], [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken], [],
            [.. ApplicationRegistry.OidcScopes], ClientSecret: config["BID_CONFORMANCE_CLIENT1_SECRET"], FirstParty: true,
            ConformanceTestClient: true, AllowClientSecretPost: true), ct);

        // Clients 1-2 for every plan; 3-4 (same secrets) register a back-channel logout URI, for that plan only: the other
        // plans' test modules do not serve that path.
        for (var i = 1; i <= 4; i++)
        {
            var secretNo = i <= 2 ? i : i - 2;
            var secret = config[$"BID_CONFORMANCE_CLIENT{secretNo}_SECRET"] ?? throw new InvalidOperationException($"BID_CONFORMANCE_CLIENT{secretNo}_SECRET is required in conformance mode.");
            await registry.EnsureClientAsync(tenant.Id, new ClientDefinition(
                $"conformance-{i}", $"OpenID conformance client {i}", ApplicationKind.Confidential,
                [suite + "/callback"], [suite + "/post_logout_redirect"],
                [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken], [], [.. ApplicationRegistry.OidcScopes],
                ClientSecret: secret, FirstParty: true, BackchannelLogoutUri: i <= 2 ? null : suite + "/backchannel_logout", ConformanceTestClient: true), ct);
        }
    }
}
