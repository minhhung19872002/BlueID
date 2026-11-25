using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Auth;

/// <summary>
/// Password storage in auth.passwords (PROMPT 4) through ASP.NET Core Identity's <see cref="IPasswordHasher{TUser}"/>
/// (docs/00 D1, D10). The newest row is the current password; older rows are kept as history.
/// </summary>
public sealed class PasswordService(BlueIdDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
{
    public const int MinLength = 12;
    public const int MaxLength = 256;

    /// <summary>Error code for a password that may not be set, or null. The common-password list comes in Phase 3.</summary>
    public static string? Validate(string password)
    {
        var length = new System.Globalization.StringInfo(password).LengthInTextElements;
        if (length < MinLength)
        {
            return "password_too_short";
        }

        return length > MaxLength ? "password_too_long" : null;
    }

    public async Task SetAsync(User user, string password, bool mustChange, CancellationToken ct)
    {
        db.Passwords.Add(new PasswordRecord { TenantId = user.TenantId, UserId = user.Id, Hash = hasher.HashPassword(user, password), MustChange = mustChange });
        user.PasswordLastChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Current password row of a user (tenant filter bypassed: sign-in runs before a tenant is known).</summary>
    public Task<PasswordRecord?> CurrentAsync(Guid userId, CancellationToken ct) =>
        db.Passwords.IgnoreQueryFilters().Where(p => p.UserId == userId && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);

    /// <summary>Checks a password; on success with an outdated hash, stores a fresh hash (PROMPT 6.1).</summary>
    public async Task<bool> VerifyAsync(User user, PasswordRecord? current, string password, CancellationToken ct)
    {
        if (current is null)
        {
            hasher.VerifyHashedPassword(user, Pbkdf2PasswordHasher.DummyHash, password);
            return false;
        }

        var result = hasher.VerifyHashedPassword(user, current.Hash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            current.Hash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }

        return result != PasswordVerificationResult.Failed;
    }
}
