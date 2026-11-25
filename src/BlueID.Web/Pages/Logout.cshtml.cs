using System.Security.Claims;
using BlueID.Application.Auth;
using BlueID.Application.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlueID.Web.Pages;

/// <summary>Sign-out ends the BlueID session row, not only the cookie. GET asks, POST (anti-forgery checked) does it.</summary>
public sealed class LogoutModel(SessionService sessions) : PageModel
{
    public bool SignedIn => User.Identity?.IsAuthenticated == true;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Guid.TryParse(User.FindFirstValue(BidClaims.Session), out var sid))
        {
            await sessions.EndAsync(sid, "signed_out", ct);
        }

        await HttpContext.SignOutAsync(SessionCookie.Scheme);
        return RedirectToPage("/Login");
    }
}
