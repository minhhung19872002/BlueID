using BlueID.Application.Auth;
using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Web;
using BlueID.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Web.Pages;

/// <summary>
/// Required password change after sign-in (seeded or reset accounts). Reached only with a pending ticket from the
/// sign-in page; the ticket names the user and the security stamp it was issued for.
/// </summary>
[EnableRateLimiting(WebRateLimits.SignIn)]
public sealed class ChangePasswordModel(PendingPasswordChange pending, IDirectoryWriter writer, SessionService sessions, BlueIdDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "t")]
    public string? Token { get; set; }

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public string? ErrorKey { get; private set; }

    public IActionResult OnGet() => pending.Read(Token) is null ? RedirectToPage("/Login") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var ticket = pending.Read(Token);
        if (ticket is null)
        {
            return RedirectToPage("/Login");
        }

        if (string.IsNullOrEmpty(NewPassword) || NewPassword != ConfirmPassword)
        {
            ErrorKey = "change_password.error_mismatch";
            return Page();
        }

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == ticket.UserId && u.DeletedAt == null, ct);
        if (user is null || user.SecurityStamp != ticket.Stamp || user.Status != Domain.UserStatus.Active)
        {
            return RedirectToPage("/Login");
        }

        try
        {
            var actor = new Actor(user.Id, null, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
            user = await writer.ChangePasswordAsync(ticket.TenantId, ticket.UserId, NewPassword, false, "required_change", actor, ct);
        }
        catch (ValidationFailedException ex)
        {
            ErrorKey = "error." + ex.Errors[0].Code;
            return Page();
        }

        var session = await sessions.CreateAsync(user, ["pwd"], HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);
        await HttpContext.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, session), new AuthenticationProperties { IsPersistent = true });
        var back = ticket.ReturnUrl is { } r && Url.IsLocalUrl(r) ? r : "/portal/";
        return LocalRedirect(back);
    }
}
