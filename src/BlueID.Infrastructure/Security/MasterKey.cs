using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BlueID.Infrastructure.Security;

/// <summary>
/// The master key from BID_MASTER_KEY (32 random bytes, base64). It never encrypts data directly: HKDF-SHA256 derives a
/// separate key per purpose ("signing-keys", "openiddict-encryption", later "totp"...), and secrets at rest are
/// AES-256-GCM with the purpose as associated data. Format: 0x01 | nonce(12) | tag(16) | ciphertext.
/// Losing the master key: docs/03.
/// </summary>
public sealed class MasterKey
{
    private const byte Version = 1;
    private readonly byte[] _key;

    public MasterKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new InvalidOperationException("BID_MASTER_KEY must be 32 bytes (base64).");
        }

        _key = key;
    }

    public static MasterKey Load(IConfiguration c)
    {
        var v = c["BID_MASTER_KEY"];
        if (string.IsNullOrWhiteSpace(v))
        {
            throw new InvalidOperationException("BID_MASTER_KEY is required (32 random bytes, base64: openssl rand -base64 32).");
        }

        try
        {
            return new MasterKey(Convert.FromBase64String(v));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("BID_MASTER_KEY is not valid base64.");
        }
    }

    public byte[] Derive(string purpose, int length) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, length, salt: "blueid/v1"u8.ToArray(), info: Encoding.UTF8.GetBytes(purpose));

    public byte[] Encrypt(string purpose, ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[1 + 12 + 16 + plaintext.Length];
        output[0] = Version;
        var nonce = output.AsSpan(1, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Derive(purpose, 32), 16);
        aes.Encrypt(nonce, plaintext, output.AsSpan(29), output.AsSpan(13, 16), Encoding.UTF8.GetBytes(purpose));
        return output;
    }

    public byte[] Decrypt(string purpose, ReadOnlySpan<byte> data)
    {
        if (data.Length < 29 || data[0] != Version)
        {
            throw new CryptographicException("Unknown secret format.");
        }

        var plaintext = new byte[data.Length - 29];
        using var aes = new AesGcm(Derive(purpose, 32), 16);
        aes.Decrypt(data.Slice(1, 12), data[29..], data.Slice(13, 16), plaintext, Encoding.UTF8.GetBytes(purpose));
        return plaintext;
    }
}
