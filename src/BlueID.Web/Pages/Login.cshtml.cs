using BlueID.Application.Auth;
using BlueID.Application.Web;
using BlueID.Infrastructure.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace BlueID.Web.Pages;

/// <summary>
/// Two-step sign-in (PROMPT 6.1): the name first (home realm discovery for federated domains comes in Phase 9), then
/// the password. Whether the account exists is never shown: the second step always appears, failure is one message.
/// </summary>
[EnableRateLimiting(WebRateLimits.SignIn)]
public sealed class LoginModel(SignInService signIn, PendingPasswordChange pending) : PageModel
{
    [BindProperty]
    public string? UserName { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool PasswordStep { get; private set; }
    public string? ErrorKey { get; private set; }

    /// <summary>OIDC login_hint passed on by the authorize endpoint: pre-fills the name (it is only a hint).</summary>
    [BindProperty(SupportsGet = true, Name = "login_hint")]
    public string? LoginHint { get; set; }

    public IActionResult OnGet()
    {
        // A signed-in user coming back from /connect/authorize (prompt=login, max_age) must sign in again; others go on.
        if (User.Identity?.IsAuthenticated == true && !(ReturnUrl?.StartsWith("/connect/authorize", StringComparison.Ordinal) ?? false))
        {
            return LocalRedirect(SafeReturnUrl());
        }

        UserName ??= LoginHint is { Length: <= 320 } h && Identifiers.TryNormalizeAddress(h, out var hint) ? hint : null;
        return Page();
    }

    public IActionResult OnPostName()
    {
        if (string.IsNullOrWhiteSpace(UserName) || !Identifiers.TryNormalizeAddress(UserName, out var upn))
        {
            ErrorKey = "login.error_name_format";
            return Page();
        }

        UserName = upn;
        PasswordStep = true;
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken ct)
    {
        PasswordStep = true;
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
        {
            ErrorKey = "login.error_invalid";
            return Page();
        }

        var outcome = await signIn.PasswordSignInAsync(new SignInRequest(UserName, Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()), ct);
        Password = null;
        if (outcome.Status != SignInStatus.Success)
        {
            ErrorKey = "login.error_invalid";
            return Page();
        }

        if (outcome.MustChangePassword)
        {
            // No session cookie until the password is changed.
            var token = pending.Issue(new PendingPasswordChange.Ticket(outcome.User!.Id, outcome.User.TenantId, outcome.User.SecurityStamp, SafeReturnUrl()));
            TempData.Clear();
            return RedirectToPage("/ChangePassword", new { t = token });
        }

        await HttpContext.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(outcome.User!, outcome.Session!),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });
        return LocalRedirect(SafeReturnUrl());
    }

    /// <summary>Only local paths: an open redirect from the sign-in page would be a phishing tool (PROMPT 7).</summary>
    private string SafeReturnUrl() =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && !ReturnUrl.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ? ReturnUrl : "/portal/";
}
