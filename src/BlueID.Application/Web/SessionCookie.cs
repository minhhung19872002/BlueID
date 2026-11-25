using System.Net;
using System.Security.Claims;
using BlueID.Application.Auth;
using BlueID.Application.Security;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BlueID.Application.Web;

public static class BidClaims
{
    public const string Subject = "sub";
    public const string Tenant = "tid";
    public const string Session = "sid";
    public const string Stamp = "bid_stamp";
}

/// <summary>
/// The BlueID browser session cookie, shared by BlueID.Web (sets it) and BlueID.Api (reads it; same Data Protection
/// key ring in PostgreSQL). Every request re-checks the session row (<see cref="SessionService.ValidateAsync"/>), so
/// disabling an account or revoking a session ends access to BlueID's own pages at once.
/// </summary>
public static class SessionCookie
{
    public const string Scheme = "bid";
    public const string CsrfHeader = "X-BlueID-CSRF";

    public static void AddBlueIdSessionCookie(this IServiceCollection services, bool plainHttpAllowed, bool isApi)
    {
        services.AddDataProtection().SetApplicationName("blueid").PersistKeysToDbContext<BlueIdDbContext>();
        services.AddAuthentication(Scheme).AddCookie(Scheme, o =>
        {
            // __Host- prefix: Secure, Path=/, no Domain (cannot be set by a sibling subdomain).
            o.Cookie.Name = plainHttpAllowed ? "bid_session" : "__Host-bid_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = plainHttpAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.Cookie.Path = "/";
            o.LoginPath = "/login";
            o.LogoutPath = "/logout";
            o.AccessDeniedPath = "/login";
            o.ExpireTimeSpan = SessionService.DefaultLifetime;
            o.SlidingExpiration = true;
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var p = ctx.Principal!;
                var ok = Guid.TryParse(p.FindFirstValue(BidClaims.Subject), out var uid) &&
                         Guid.TryParse(p.FindFirstValue(BidClaims.Session), out var sid) &&
                         (await ctx.HttpContext.RequestServices.GetRequiredService<SessionService>()
                             .ValidateAsync(uid, sid, p.FindFirstValue(BidClaims.Stamp) ?? "", ctx.HttpContext.RequestAborted)).IsValid;
                if (!ok)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(Scheme);
                }
            };
            if (isApi)
            {
                // The API answers 401/403 in its envelope instead of redirecting to the sign-in page.
                o.Events.OnRedirectToLogin = ctx => ApiResponses.WriteErrorAsync(ctx.HttpContext, HttpStatusCode.Unauthorized, "unauthorized");
                o.Events.OnRedirectToAccessDenied = ctx => ApiResponses.WriteErrorAsync(ctx.HttpContext, HttpStatusCode.Forbidden, "forbidden");
            }
        });
        services.AddAuthorization();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
    }

    public static ClaimsPrincipal Principal(User user, Session session)
    {
        var id = new ClaimsIdentity(Scheme, BidClaims.Subject, null);
        id.AddClaim(new Claim(BidClaims.Subject, user.Id.ToString("D")));
        id.AddClaim(new Claim(BidClaims.Tenant, user.TenantId.ToString("D")));
        id.AddClaim(new Claim(BidClaims.Session, session.Id.ToString("D")));
        id.AddClaim(new Claim(BidClaims.Stamp, user.SecurityStamp));
        return new ClaimsPrincipal(id);
    }
}

/// <summary>Sets the tenant of the request from the validated session (runs after authentication).</summary>
public sealed class TenantFromSessionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http, ITenantContext tenant)
    {
        if (http.User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(http.User.FindFirstValue(BidClaims.Tenant), out var tid) &&
            Guid.TryParse(http.User.FindFirstValue(BidClaims.Subject), out var uid))
        {
            tenant.Set(tid, uid);
        }

        return next(http);
    }
}

/// <summary>
/// Cookie-authenticated API calls that change state must carry the custom header: a cross-site form cannot set it and a
/// cross-origin script needs a CORS pre-flight that BlueID never allows (OWASP CSRF "custom request header").
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method) &&
            http.Request.Cookies.Keys.Any(k => k.EndsWith("bid_session", StringComparison.Ordinal)) &&
            !http.Request.Headers.ContainsKey(SessionCookie.CsrfHeader))
        {
            return ApiResponses.WriteErrorAsync(http, HttpStatusCode.Forbidden, "csrf_header_missing");
        }

        return next(http);
    }
}

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    Guid SessionId { get; }
    Directory.Actor Actor { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

    public Guid UserId => Guid.Parse(Http.User.FindFirstValue(BidClaims.Subject) ?? throw new UnauthorizedAccessException());
    public Guid TenantId => Guid.Parse(Http.User.FindFirstValue(BidClaims.Tenant) ?? throw new UnauthorizedAccessException());
    public Guid SessionId => Guid.Parse(Http.User.FindFirstValue(BidClaims.Session) ?? throw new UnauthorizedAccessException());

    public Directory.Actor Actor => new(UserId, null, Http.Connection.RemoteIpAddress?.ToString(),
        Http.Request.Headers.UserAgent.ToString());
}

/// <summary>Endpoint needs a signed-in user holding this permission in their tenant (lesson 3: [Authorize] alone is not a guard).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAsyncAuthorizationFilter
{
    public string Permission { get; } = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true || !Guid.TryParse(http.User.FindFirstValue(BidClaims.Subject), out var uid))
        {
            context.Result = new ChallengeResult(SessionCookie.Scheme);
            return;
        }

        var perms = await http.RequestServices.GetRequiredService<PermissionService>().GetAsync(uid, http.RequestAborted);
        if (!perms.Contains(Permission))
        {
            context.Result = new ObjectResult(ApiResponses.Error(http, "forbidden")) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}

/// <summary>Endpoint acts only on the caller's own data (/me): needs a session, the handler scopes by the caller's id.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SelfGuardedAttribute(string reason) : Attribute, IAuthorizationFilter
{
    public string Reason { get; } = reason;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult(SessionCookie.Scheme);
        }
    }
}
