using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlueID.Infrastructure.Text;

/// <summary>
/// Normalisation of names and addresses (PROMPT 0.2): NFC for every human-typed identifier; domain part converted to
/// its A-label (IDNA2008 / UTS #46 through <see cref="IdnMapping"/>, which uses ICU) and lower-cased; the local part
/// keeps its case (RFC 5321) but sign-in names compare case-insensitively (citext column).
/// </summary>
public static class Identifiers
{
    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true, AllowUnassigned = false };

    /// <summary>Strips U+0000 and other C0 controls except tab, trims, NFC. Throws on invalid UTF-16 (lone surrogates).</summary>
    public static string CleanText(string value)
    {
        if (!IsWellFormed(value))
        {
            throw new FormatException("Text is not well-formed Unicode.");
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c >= ' ' || c == '\t')
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    public static bool IsWellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A-label of a domain, lower case, no trailing dot; false when not a valid IDNA domain.</summary>
    public static bool TryNormalizeDomain(string input, out string domain)
    {
        domain = "";
        var s = CleanText(input).TrimEnd('.');
        if (s.Length == 0 || s.Length > 253 || s.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var ascii = Idn.GetAscii(s).ToLowerInvariant();
            if (ascii.Split('.').Any(l => l.Length is 0 or > 63) || !ascii.Contains('.', StringComparison.Ordinal))
            {
                return false;
            }

            domain = ascii;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>U-label form of a stored A-label domain, for display.</summary>
    public static string DomainForDisplay(string asciiDomain)
    {
        try
        {
            return Idn.GetUnicode(asciiDomain);
        }
        catch (ArgumentException)
        {
            return asciiDomain;
        }
    }

    /// <summary>
    /// Normalised address: NFC local part (RFC 6531 allows UTF-8), A-label lower-case domain. False when the shape is
    /// not local@domain or a part is too long (local 64 octets, total 254).
    /// </summary>
    public static bool TryNormalizeAddress(string input, out string address)
    {
        address = "";
        string s;
        try
        {
            s = CleanText(input);
        }
        catch (FormatException)
        {
            return false;
        }

        var at = s.LastIndexOf('@');
        if (at <= 0 || at == s.Length - 1)
        {
            return false;
        }

        var local = s[..at];
        if (Encoding.UTF8.GetByteCount(local) > 64 || local.Any(c => char.IsWhiteSpace(c) || c is '@' or '"' or '\\' or '<' or '>' or ',' or ';' or ':' or '(' or ')' or '[' or ']') ||
            local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryNormalizeDomain(s[(at + 1)..], out var domain))
        {
            return false;
        }

        address = local + "@" + domain;
        return Encoding.UTF8.GetByteCount(address) <= 254;
    }

    public static string DomainOf(string normalizedAddress) => normalizedAddress[(normalizedAddress.LastIndexOf('@') + 1)..];

    /// <summary>SHA-256 of the lower-cased normalised sign-in name (sign_in_logs keeps no typed names).</summary>
    public static byte[] HashForLog(string typed)
    {
        var norm = TryNormalizeAddress(typed, out var a) ? a : typed;
        return SHA256.HashData(Encoding.UTF8.GetBytes(norm.ToLowerInvariant()));
    }
}
