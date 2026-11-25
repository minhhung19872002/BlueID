using System.Security.Cryptography;
using System.Text;
using BlueID.Domain;
using Microsoft.AspNetCore.Identity;

namespace BlueID.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 from the BCL (docs/00 D1). Format: <c>$pbkdf2-sha512$i=&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>
/// (base64 without padding). Parameters are read back from the string, so raising the iteration count later keeps old
/// hashes working and asks for a rehash. Values outside sane bounds (a tampered row) are treated as a wrong password.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher<User>
{
    public const string Prefix = "$pbkdf2-sha512$";
    /// <summary>OWASP Password Storage Cheat Sheet, PBKDF2-HMAC-SHA512.</summary>
    public const int CurrentIterations = 210_000;
    private const int MinIterations = 100_000;
    private const int MaxIterations = 5_000_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 64;

    /// <summary>A fixed hash verified when the account does not exist, so both answers take the same time.</summary>
    public static readonly string DummyHash = Hash("not-a-real-password-" + Guid.NewGuid().ToString("N"));

    public string HashPassword(User user, string password) => Hash(password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword) =>
        Verify(hashedPassword, providedPassword);

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, CurrentIterations);
        return $"{Prefix}i={CurrentIterations}${B64(salt)}${B64(hash)}";
    }

    public static PasswordVerificationResult Verify(string stored, string provided)
    {
        if (!TryParse(stored, out var iterations, out var salt, out var expected))
        {
            // Still spend the time of a real check: no timing difference for malformed rows.
            Derive(provided, new byte[SaltBytes], CurrentIterations);
            return PasswordVerificationResult.Failed;
        }

        var actual = Derive(provided, salt, iterations);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < CurrentIterations ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)), salt, iterations,
            HashAlgorithmName.SHA512, HashBytes);

    private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = stored[Prefix.Length..].Split('$');
        if (parts.Length != 3 || !parts[0].StartsWith("i=", StringComparison.Ordinal) ||
            !int.TryParse(parts[0].AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations) ||
            iterations < MinIterations || iterations > MaxIterations)
        {
            return false;
        }

        try
        {
            salt = FromB64(parts[1]);
            hash = FromB64(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltBytes && hash.Length == HashBytes;
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=');

    private static byte[] FromB64(string s) => Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
}
