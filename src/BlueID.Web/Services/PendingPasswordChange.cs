using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BlueID.Web;

/// <summary>
/// After a correct password that must be changed, the browser gets no session cookie: only this short-lived, signed
/// and encrypted token (Data Protection, 10 minutes) that allows exactly one password change for that user.
/// </summary>
public sealed class PendingPasswordChange(IDataProtectionProvider provider, TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("blueid.pending-password-change").ToTimeLimitedDataProtector();

    public sealed record Ticket(Guid UserId, Guid TenantId, string Stamp, string? ReturnUrl);

    public string Issue(Ticket t) => protector.Protect(JsonSerializer.Serialize(t), clock.GetUtcNow() + Lifetime);

    public Ticket? Read(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Ticket>(protector.Unprotect(token, out var expires)) is { } t && expires > clock.GetUtcNow() ? t : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
