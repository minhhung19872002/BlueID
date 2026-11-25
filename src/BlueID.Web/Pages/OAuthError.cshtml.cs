using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlueID.Web.Pages;

/// <summary>
/// Shown when a sign-in request from an application is invalid and cannot be sent back to it (no valid redirect URI,
/// missing response type...). Only a stable error code is shown; the application's developer finds the meaning in RFC 6749.
/// </summary>
public sealed class OAuthErrorModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Error { get; set; }

    public string Code => Error is { Length: <= 64 } e && e.All(c => char.IsAsciiLetterLower(c) || c == '_') ? e : "invalid_request";

    public void OnGet() => Response.StatusCode = StatusCodes.Status400BadRequest;
}
