using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SampleProduct;

// A pretend Blue product (PROMPT 8): signs in through BlueID (code + PKCE + resource + private_key_jwt), ends its local
// session on back-channel logout, and exposes an API that validates BlueID access tokens the way docs/05 5.2 says.
// Developer sample: its few page texts are inline English on purpose (not a shipped UI).
var builder = WebApplication.CreateBuilder(args);
var cfg = SampleSettings.Load(builder.Configuration);
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton<RevokedSessions>();
builder.Services.AddSingleton(new BackchannelHandlerHolder(cfg.CreateHandler()));

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "sample_session";
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Events.OnValidatePrincipal = ctx =>
        {
            // Back-channel logout: a BlueID session that ended ends this local session too.
            var revoked = ctx.HttpContext.RequestServices.GetRequiredService<RevokedSessions>();
            if (ctx.Principal?.FindFirstValue("sid") is { } sid && revoked.Contains(sid))
            {
                ctx.RejectPrincipal();
                return ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = cfg.Authority;
        o.RequireHttpsMetadata = cfg.Authority.StartsWith("https://", StringComparison.Ordinal);
        o.ClientId = cfg.ClientId;
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.MapInboundClaims = false;
        o.SaveTokens = true;
        o.GetClaimsFromUserInfoEndpoint = false;
        o.Scope.Clear();
        foreach (var s in SampleSettings.Scopes)
        {
            o.Scope.Add(s);
        }

        o.TokenValidationParameters.NameClaimType = "name";
        o.Events.OnRedirectToIdentityProvider = ctx =>
        {
            ctx.ProtocolMessage.SetParameter("resource", cfg.ApiResource);
            return Task.CompletedTask;
        };
        o.Events.OnAuthorizationCodeReceived = ctx =>
        {
            // private_key_jwt (RFC 7523): no shared secret leaves this product.
            ctx.TokenEndpointRequest!.ClientSecret = null;
            ctx.TokenEndpointRequest.ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            ctx.TokenEndpointRequest.ClientAssertion = cfg.CreateClientAssertion(ctx.Options.Configuration!.Issuer);
            ctx.TokenEndpointRequest.SetParameter("resource", cfg.ApiResource);
            return Task.CompletedTask;
        };
    })
    .AddJwtBearer("api", o =>
    {
        o.Authority = cfg.Authority;
        o.RequireHttpsMetadata = cfg.Authority.StartsWith("https://", StringComparison.Ordinal);
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            // docs/05 5.2: only RS256/ES256, typ at+jwt, exact issuer, our audience, 60 s clock skew.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
            ValidTypes = ["at+jwt"],
            ValidAudience = cfg.ApiResource,
            ClockSkew = TimeSpan.FromSeconds(60),
            NameClaimType = "name",
        };
    });
builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
    .Configure<BackchannelHandlerHolder>((o, h) => o.BackchannelHttpHandler = h.Handler);
builder.Services.AddOptions<JwtBearerOptions>("api")
    .Configure<BackchannelHandlerHolder>((o, h) => o.BackchannelHttpHandler = h.Handler);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost, KnownIPNetworks = { }, KnownProxies = { } });
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext http) =>
{
    var u = http.User;
    var body = u.Identity?.IsAuthenticated == true
        ? $"<h1>Signed in</h1><dl><dt>name</dt><dd id=\"name\">{Enc(u.FindFirstValue("name"))}</dd><dt>sub</dt><dd id=\"sub\">{Enc(u.FindFirstValue("sub"))}</dd>" +
          $"<dt>tid</dt><dd id=\"tid\">{Enc(u.FindFirstValue("tid"))}</dd><dt>sid</dt><dd id=\"sid\">{Enc(u.FindFirstValue("sid"))}</dd></dl><p><a href=\"/signout\">Sign out</a></p>"
        : "<h1>Sample product</h1><p><a id=\"signin\" href=\"/signin\">Sign in with BlueID</a></p>";
    return Results.Content($"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Sample product</title><body>{body}</body></html>", "text/html; charset=utf-8");
});
app.MapGet("/signin", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OpenIdConnectDefaults.AuthenticationScheme]));
app.MapGet("/signout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

// OIDC Back-Channel Logout 1.0, section 2.6 checks.
app.MapPost("/blueid/backchannel-logout", async (HttpContext http, RevokedSessions revoked, ILogger<RevokedSessions> log) =>
{
    var form = await http.Request.ReadFormAsync();
    var token = form["logout_token"].ToString();
    var oidc = http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
    var config = await oidc.ConfigurationManager!.GetConfigurationAsync(http.RequestAborted);
    var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
    {
        ValidIssuer = config.Issuer,
        ValidAudience = cfg.ClientId,
        IssuerSigningKeys = config.SigningKeys,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
        ValidTypes = ["logout+jwt"],
        ClockSkew = TimeSpan.FromSeconds(60),
    });
    if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt || !HasLogoutEvent(jwt) ||
        jwt.TryGetPayloadValue<string>("nonce", out _) || !jwt.TryGetPayloadValue<string>("sid", out var sid))
    {
        log.LogWarning("Refused logout token: {Error}", result.Exception?.Message ?? "claims");
        return Results.BadRequest();
    }

    revoked.Add(sid);
    log.LogInformation("Back-channel logout for session {Sid}", sid);
    return Results.Ok();
}).DisableAntiforgery();

// The product's API: accepts only BlueID access tokens issued for it.
app.MapGet("/api/whoami", (ClaimsPrincipal user) => Results.Ok(new
{
    sub = user.FindFirstValue("sub"),
    tid = user.FindFirstValue("tid"),
    scp = user.FindFirstValue("scp"),
    idtyp = user.FindFirstValue("idtyp"),
})).RequireAuthorization(p => p.AddAuthenticationSchemes("api").RequireAuthenticatedUser());

app.Run();

static string Enc(string? s) => HtmlEncoder.Default.Encode(s ?? "");

// "events" is a JSON object; it must name the back-channel logout event (OIDC Back-Channel Logout 1.0, 2.4).
static bool HasLogoutEvent(JsonWebToken jwt)
{
    if (!jwt.TryGetClaim("events", out var claim))
    {
        return false;
    }

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(claim.Value);
        return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out _);
    }
    catch (System.Text.Json.JsonException)
    {
        return false;
    }
}

namespace SampleProduct
{
    public sealed class RevokedSessions
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _sids = new(StringComparer.Ordinal);

        public void Add(string sid) => _sids[sid] = DateTimeOffset.UtcNow;

        public bool Contains(string sid) => _sids.ContainsKey(sid);
    }

    /// <summary>The HTTP handler used to reach BlueID (tests replace it with the test server's handler).</summary>
    public sealed record BackchannelHandlerHolder(HttpMessageHandler Handler);

    public sealed class SampleSettings
    {
        public static readonly string[] Scopes = ["openid", "profile", "email", "offline_access", "sample.read"];

        public required string Authority { get; init; }
        public required string ClientId { get; init; }
        public required string ApiResource { get; init; }
        public required string KeyId { get; init; }
        public required RSA PrivateKey { get; init; }
        public string? TrustedCaFile { get; init; }

        public static SampleSettings Load(IConfiguration c)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(c["SAMPLE_KEY_FILE"] ?? throw new InvalidOperationException("SAMPLE_KEY_FILE is required.")));
            return new SampleSettings
            {
                Authority = c["SAMPLE_AUTHORITY"] ?? throw new InvalidOperationException("SAMPLE_AUTHORITY is required."),
                ClientId = c["SAMPLE_CLIENT_ID"] ?? "sample-product",
                ApiResource = c["SAMPLE_API"] ?? throw new InvalidOperationException("SAMPLE_API is required."),
                KeyId = c["SAMPLE_KEY_ID"] ?? "sample-dev-1",
                PrivateKey = rsa,
                TrustedCaFile = c["SAMPLE_CA_FILE"],
                DevHosts = c["SAMPLE_DEV_HOSTS"],
            };
        }

        /// <summary>RFC 7523 client assertion: iss = sub = client_id, aud = issuer, unique jti, 2 minutes.</summary>
        public string CreateClientAssertion(string issuer) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ClientId,
            Audience = issuer,
            Expires = DateTime.UtcNow.AddMinutes(2),
            // draft-ietf-oauth-rfc7523bis explicit type, required by BlueID (docs/05 4).
            TokenType = "client-authentication+jwt",
            Claims = new Dictionary<string, object> { ["sub"] = ClientId, ["jti"] = Guid.NewGuid().ToString("N") },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(PrivateKey) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });

        /// <summary>SAMPLE_DEV_HOSTS "id.blue.test=127.0.0.1": dev test domains without editing the hosts file.</summary>
        public string? DevHosts { get; init; }

        private void ApplyDevHosts(SocketsHttpHandler handler)
        {
            var map = (DevHosts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => IPAddress.Parse(p[1]), StringComparer.OrdinalIgnoreCase);
            if (map.Count == 0)
            {
                return;
            }

            handler.ConnectCallback = async (ctx, ct) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                try
                {
                    await (map.TryGetValue(ctx.DnsEndPoint.Host, out var ip)
                        ? socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct)
                        : socket.ConnectAsync(ctx.DnsEndPoint, ct));
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        /// <summary>Dev: trust the Caddy internal CA for BlueID's certificate (and nothing else extra).</summary>
        public HttpMessageHandler CreateHandler()
        {
            var handler = new SocketsHttpHandler();
            ApplyDevHosts(handler);
            if (TrustedCaFile is { Length: > 0 } file)
            {
                var ca = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(file);
                handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None)
                    {
                        return true;
                    }

                    if (cert is null || chain is null)
                    {
                        return false;
                    }

                    using var custom = new System.Security.Cryptography.X509Certificates.X509Chain();
                    custom.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    custom.ChainPolicy.CustomTrustStore.Add(ca);
                    custom.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    return custom.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert));
                };
            }

            return handler;
        }
    }
}
