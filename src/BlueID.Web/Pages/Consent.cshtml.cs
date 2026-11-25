using System.Security.Claims;
using System.Text.Json;
using BlueID.Application.OAuth;
using BlueID.Application.Web;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace BlueID.Web.Pages;

/// <summary>
/// Consent for third-party applications (PROMPT 6.1): application name, publisher and the permissions asked. Blue
/// products (first party) never reach this page. The decision goes back to /connect/authorize.
/// </summary>
public sealed class ConsentModel(BlueIdDbContext db, IOpenIddictScopeManager scopeManager, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public ClientApplication? App { get; private set; }
    public IReadOnlyList<(string Scope, string Text)> Permissions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : Redirect("/login");

    public async Task<IActionResult> OnPostAsync(string decision, CancellationToken ct)
    {
        var cookie = await HttpContext.AuthenticateAsync(SessionCookie.Scheme);
        if (!cookie.Succeeded || !await LoadAsync(ct))
        {
            return Redirect("/login");
        }

        if (decision != "allow")
        {
            return LocalRedirect(ReturnUrl + "&" + Controllers.OidcController.DeniedMarker + "=1");
        }

        var userId = Guid.Parse(cookie.Principal!.FindFirstValue(BidClaims.Subject)!);
        var tenantId = Guid.Parse(cookie.Principal!.FindFirstValue(BidClaims.Tenant)!);
        var scopes = Permissions.Select(p => p.Scope).ToHashSet(StringComparer.Ordinal);
        var existing = await db.Consents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ApplicationId == App!.Id && c.TenantId == tenantId && c.UserId == userId && c.RevokedAt == null && c.DeletedAt == null, ct);
        if (existing is null)
        {
            db.Consents.Add(new Consent { TenantId = tenantId, ApplicationId = App!.Id, UserId = userId, Scopes = JsonSerializer.Serialize(scopes), GrantedAt = clock.GetUtcNow() });
        }
        else
        {
            scopes.UnionWith(JsonSerializer.Deserialize<string[]>(existing.Scopes) ?? []);
            existing.Scopes = JsonSerializer.Serialize(scopes.Order(StringComparer.Ordinal));
            existing.GrantedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return LocalRedirect(ReturnUrl!);
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        // Only a local authorize URL comes back here (no open redirect).
        if (string.IsNullOrEmpty(ReturnUrl) || !ReturnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal) || !Url.IsLocalUrl(ReturnUrl))
        {
            return false;
        }

        var query = QueryHelpers.ParseQuery(new Uri("http://x" + ReturnUrl).Query);
        var clientId = query.TryGetValue("client_id", out var c) ? c.ToString() : null;
        App = await db.ClientApplications.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId && a.DeletedAt == null && a.Enabled, ct);
        if (App is null)
        {
            return false;
        }

        var list = new List<(string, string)>();
        foreach (var scope in (query.TryGetValue("scope", out var s) ? s.ToString() : "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            var text = ApplicationRegistry.OidcScopes.Contains(scope, StringComparer.Ordinal)
                ? "consent.scope." + scope.Replace('.', '_')
                : (await scopeManager.FindByNameAsync(scope, ct) is { } entity ? await scopeManager.GetDescriptionAsync(entity, ct) : null) ?? scope;
            list.Add((scope, text));
        }

        Permissions = list;
        return true;
    }
}
