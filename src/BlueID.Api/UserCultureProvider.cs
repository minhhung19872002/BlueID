using System.Security.Claims;
using BlueID.Application.Web;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Api;

/// <summary>Language of API messages: the user's setting, then the tenant default; else the next providers (Accept-Language).</summary>
public sealed class UserCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Localisation runs before authentication: read the cookie ticket directly (validated again later in the pipeline).
        var auth = await httpContext.AuthenticateAsync(SessionCookie.Scheme);
        if (!auth.Succeeded || !Guid.TryParse(auth.Principal.FindFirstValue(BidClaims.Subject), out var uid))
        {
            return null;
        }

        var db = httpContext.RequestServices.GetRequiredService<BlueIdDbContext>();
        var row = await (from u in db.Users.IgnoreQueryFilters()
                         join t in db.Tenants.IgnoreQueryFilters() on u.TenantId equals t.Id
                         where u.Id == uid
                         select new { u.Locale, t.DefaultLocale }).AsNoTracking().FirstOrDefaultAsync(httpContext.RequestAborted);
        var pick = row?.Locale ?? row?.DefaultLocale;
        return pick is null ? null : new ProviderCultureResult(pick);
    }
}
