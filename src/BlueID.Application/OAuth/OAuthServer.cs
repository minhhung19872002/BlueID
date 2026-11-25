using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace BlueID.Application.OAuth;

public static class OAuthServer
{
    public const string ConfigurationPath = ".well-known/openid-configuration";
    public const string AuthorizationServerMetadataPath = ".well-known/oauth-authorization-server";
    public const string JwksPath = ".well-known/jwks.json";

    /// <summary>OpenIddict core (stores) and the key ring: every process, so the registry and keys work everywhere.</summary>
    public static IServiceCollection AddBlueIdOAuthCore(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(MasterKey.Load(config));
        services.AddSingleton<KeyRing>();
        services.AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<BlueIdDbContext>().ReplaceDefaultEntities<Guid>());
        services.AddScoped<ApplicationRegistry>();
        services.AddScoped<BackchannelLogoutQueue>();
        return services;
    }

    /// <summary>
    /// The OAuth 2.0 / OIDC server (PROMPT 3, docs/05 2-6). Only what the contract lists is enabled; everything else is
    /// refused with the standard error (implicit, hybrid, password, PKCE plain, client_secret_post...).
    /// </summary>
    public static IServiceCollection AddBlueIdOAuthServer(this IServiceCollection services, BidSettings settings, bool plainHttpAllowed, bool conformanceMode,
        string? devHosts = null)
    {
        services.AddOpenIddict().AddServer(o =>
        {
            o.SetIssuer(new Uri(settings.Issuer + "/"));
            o.SetAuthorizationEndpointUris("connect/authorize")
                .SetPushedAuthorizationEndpointUris("connect/par")
                .SetTokenEndpointUris("connect/token")
                .SetUserInfoEndpointUris("connect/userinfo")
                .SetEndSessionEndpointUris("connect/logout")
                .SetIntrospectionEndpointUris("connect/introspect")
                .SetRevocationEndpointUris("connect/revoke")
                .SetConfigurationEndpointUris(ConfigurationPath, AuthorizationServerMetadataPath)
                .SetJsonWebKeySetEndpointUris(JwksPath);
            o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowClientCredentialsFlow();
            o.RegisterScopes([.. ApplicationRegistry.OidcScopes]);
            o.RegisterClaims(Claims.Subject, TokenClaims.Tenant, TokenClaims.Session, TokenClaims.AccountKind, TokenClaims.HomeTenant, TokenClaims.IdentityType,
                Claims.AuthenticationTime, Claims.AuthenticationMethodReference, Claims.AuthenticationContextReference, Claims.Name, Claims.GivenName,
                Claims.FamilyName, Claims.PreferredUsername, Claims.Email, Claims.EmailVerified, Claims.Locale, Claims.Zoneinfo, TokenClaims.HourCycle);
            o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
                .SetIdentityTokenLifetime(TimeSpan.FromMinutes(15))
                .SetAuthorizationCodeLifetime(TimeSpan.FromSeconds(60))
                // Rolling refresh tokens, 14 days without use (sliding); the 90-day cap is checked at the token endpoint.
                .SetRefreshTokenLifetime(TimeSpan.FromDays(14))
                // A refresh token is good for exactly one exchange: a second use revokes the whole chain (PROMPT 3).
                .SetRefreshTokenReuseLeeway(TimeSpan.Zero);
            // Resources live in apps.resources and are added at run time; OidcController validates the resource parameter
            // itself (unknown, more than one, scope of another resource) and the client's rsrc: permission is still checked.
            o.DisableResourceValidation();
            // Products read access tokens (RFC 9068 JWT, signed, not encrypted).
            o.DisableAccessTokenEncryption();
            // Request objects (OIDC Core 6) are not supported. OpenIddict refuses them while validating, and errors raised
            // while validating are never sent back to the client (the redirect URI counts as validated only afterwards).
            // OIDC Core 6.1 wants request_not_supported at the client: OidcController refuses them instead (BUG-014).
            o.RemoveEventHandler(OpenIddict.Server.OpenIddictServerHandlers.Authentication.ValidateRequestParameter.Descriptor);
            // docs/00 D16: client_secret_post only for applications registered with it.
            o.AddEventHandler<ProcessAuthenticationContext>(b => b.UseScopedHandler<ClientSecretPostGuard>()
                .SetOrder(OpenIddict.Server.OpenIddictServerHandlers.ValidateAuthenticationDemand.Descriptor.Order + 1));
            o.AddEventHandler<HandleConfigurationRequestContext>(b => b.UseInlineHandler(ctx =>
            {
                ctx.Metadata["backchannel_logout_supported"] = true;
                ctx.Metadata["backchannel_logout_session_supported"] = true;
                ctx.Metadata["acr_values_supported"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { TokenClaims.AcrSingleFactor, TokenClaims.AcrMultiFactor });
                // Request objects are not supported: no algorithm at all (request_parameter_supported is false too).
                ctx.Metadata["request_object_signing_alg_values_supported"] = System.Text.Json.JsonSerializer.SerializeToElement(Array.Empty<string>());
                return default;
            }));
            o.AddEventHandler<ApplyConfigurationResponseContext>(b => b.UseInlineHandler(ctx =>
            {
                // Not defined by RFC 9126 / RFC 8414 (the conformance suite flags it); OpenIddict adds it late, so it is
                // removed from the response just before it is written.
                ctx.Response.RemoveParameter("pushed_authorization_request_endpoint_auth_methods_supported");
                return default;
            }).SetOrder(int.MinValue + 100_000));
            var aspnet = o.UseAspNetCore()
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .EnableUserInfoEndpointPassthrough()
                .EnableEndSessionEndpointPassthrough()
                // OpenIddict decides whether an error can go back to the client; when it cannot (no valid redirect_uri,
                // missing response_type...), it leaves the body empty and BlueID.Web shows its translated error page.
                .EnableStatusCodePagesIntegration();
            if (plainHttpAllowed)
            {
                aspnet.DisableTransportSecurityRequirement();
            }
        });
        services.AddSingleton<KeyRingServerOptions>();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>>(sp => sp.GetRequiredService<KeyRingServerOptions>());
        services.AddSingleton<IOptionsChangeTokenSource<OpenIddictServerOptions>>(sp => sp.GetRequiredService<KeyRingServerOptions>());
        services.AddSingleton<IPostConfigureOptions<OpenIddictServerOptions>, ContractRestrictions>();
        services.AddHostedService<KeyRingRefresher>();
        services.AddSingleton<BackchannelLogoutSender>();
        services.AddHostedService(sp => sp.GetRequiredService<BackchannelLogoutSender>());
        services.AddHttpClient(BackchannelLogoutSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => WithDevHosts(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                // Conformance suite only (docs/00 D14): its back-channel endpoint has a self-signed certificate.
#pragma warning disable CA5359 // only when BID_CONFORMANCE_MODE=true, never in production (docs/00 D14)
                SslOptions = conformanceMode ? new System.Net.Security.SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true } : new(),
#pragma warning restore CA5359
            }, devHosts));
        return services;
    }

    private static SocketsHttpHandler WithDevHosts(SocketsHttpHandler h, string? devHosts)
    {
        Infrastructure.Net.DevHosts.Apply(h, Infrastructure.Net.DevHosts.Parse(devHosts));
        return h;
    }

    /// <summary>Removes what OpenIddict allows by default but the contract does not (docs/05 4).</summary>
    private sealed class ContractRestrictions : IPostConfigureOptions<OpenIddictServerOptions>
    {
        public void PostConfigure(string? name, OpenIddictServerOptions options)
        {
            options.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain);
            options.ClientAuthenticationMethods.Remove(ClientAuthenticationMethods.ClientSecretJwt);
            options.ResponseModes.Remove(ResponseModes.Fragment);
        }
    }
}
