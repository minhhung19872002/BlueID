using System.Security.Cryptography;
using System.Text.Json;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace BlueID.Infrastructure.Security;

/// <summary>One loaded signing key.</summary>
public sealed record LoadedKey(string Kid, string Algorithm, SigningKeyStatus Status, SecurityKey Key);

/// <summary>
/// Signing keys of the deployment (PROMPT 7, docs/00 D15). Every node keeps the current set in memory and reloads it from
/// keys.signing_keys every <see cref="RefreshInterval"/>; when the set changes, <see cref="GetChangeToken"/> fires and
/// OpenIddict rebuilds its options, so a rotation reaches every node without a restart. Rotation itself runs on one node
/// at a time (advisory lock) in <see cref="RotateAsync"/>.
/// </summary>
public sealed class KeyRing(IServiceScopeFactory scopes, MasterKey master, TimeProvider clock, ILogger<KeyRing> log) : IDisposable
{
    public const string Purpose = "signing-keys";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    /// <summary>A key is published at least this long before it signs (PROMPT 7).</summary>
    public static readonly TimeSpan PublishBeforeUse = TimeSpan.FromHours(48);
    public static readonly TimeSpan ActiveFor = TimeSpan.FromDays(90);
    /// <summary>Kept for validation and in JWKS after it stops signing: the longest token it may have signed (refresh tokens, 90 days) plus margin.</summary>
    public static readonly TimeSpan RetireAfter = TimeSpan.FromDays(91);
    private const long RotationLockKey = 0x426C75654944_4B52; // "BlueID" + "KR"

    private volatile IReadOnlyList<LoadedKey> _keys = [];
    private CancellationTokenSource _changed = new();
    private readonly Lock _gate = new();

    public IReadOnlyList<LoadedKey> Keys => _keys;

    /// <summary>The key that signs now.</summary>
    public LoadedKey Active => _keys.FirstOrDefault(k => k.Status == SigningKeyStatus.Active) ?? throw new InvalidOperationException("No active signing key.");

    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return new CancellationChangeToken(_changed.Token);
        }
    }

    /// <summary>First start of a deployment: an ACTIVE key (nothing was published before, so no 48 h wait) and its NEXT.</summary>
    public async Task EnsureAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [RotationLockKey], ct);
        var now = clock.GetUtcNow();
        if (!await db.SigningKeys.AnyAsync(k => k.Status == SigningKeyStatus.Active, ct))
        {
            db.SigningKeys.Add(NewKey(SigningKeyStatus.Active, now));
            log.LogWarning("No active signing key: created one (first start of this deployment)");
        }

        if (!await db.SigningKeys.AnyAsync(k => k.Status == SigningKeyStatus.Next, ct))
        {
            db.SigningKeys.Add(NewKey(SigningKeyStatus.Next, now));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Scheduled rotation: NEXT becomes ACTIVE once ACTIVE is 90 days old and NEXT has been published for 48 hours; the old
    /// ACTIVE becomes RETIRING (still published and accepted) until <see cref="RetireAfter"/>; a new NEXT is created.
    /// Returns true when something changed.
    /// </summary>
    public async Task<bool> RotateAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [RotationLockKey], ct);
        var now = clock.GetUtcNow();
        var changed = false;
        foreach (var old in await db.SigningKeys.Where(k => k.Status == SigningKeyStatus.Retiring && k.RetireAt <= now).ToListAsync(ct))
        {
            old.Status = SigningKeyStatus.Retired;
            old.PrivateKeyEncrypted = []; // nothing can sign with it any more
            changed = true;
        }

        var active = await db.SigningKeys.FirstOrDefaultAsync(k => k.Status == SigningKeyStatus.Active, ct);
        var next = await db.SigningKeys.FirstOrDefaultAsync(k => k.Status == SigningKeyStatus.Next, ct);
        if (active is not null && next is not null && active.ActivatedAt + ActiveFor <= now && next.CreatedAt + PublishBeforeUse <= now)
        {
            active.Status = SigningKeyStatus.Retiring;
            active.RetireAt = now + RetireAfter;
            await db.SaveChangesAsync(ct); // free the "one ACTIVE" slot before promoting
            next.Status = SigningKeyStatus.Active;
            next.ActivatedAt = now;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Signing key rotated: {Old} retiring, {New} active", active.Kid, next.Kid);
            next = null;
            changed = true;
        }

        if (next is null && !await db.SigningKeys.AnyAsync(k => k.Status == SigningKeyStatus.Next, ct))
        {
            db.SigningKeys.Add(NewKey(SigningKeyStatus.Next, now));
            changed = true;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (changed)
        {
            await ReloadAsync(ct);
        }

        return changed;
    }

    /// <summary>Reads the published keys; fires the change token when the set (kid + status) differs.</summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        var rows = await db.SigningKeys.AsNoTracking()
            .Where(k => k.Status == SigningKeyStatus.Active || k.Status == SigningKeyStatus.Next || k.Status == SigningKeyStatus.Retiring)
            .ToListAsync(ct);
        // ACTIVE first: OpenIddict signs with the first credential of the list (docs/00 D15).
        var ordered = rows.OrderBy(r => r.Status switch { SigningKeyStatus.Active => 0, SigningKeyStatus.Next => 1, _ => 2 }).ThenBy(r => r.CreatedAt).ToList();
        var signature = string.Join('|', ordered.Select(r => r.Kid + ":" + r.Status));
        if (signature == string.Join('|', _keys.Select(k => k.Kid + ":" + k.Status)))
        {
            return;
        }

        _keys = ordered.Select(r => new LoadedKey(r.Kid, r.Algorithm, r.Status, ToSecurityKey(r))).ToList();
        CancellationTokenSource previous;
        lock (_gate)
        {
            previous = _changed;
            _changed = new CancellationTokenSource();
        }

        log.LogInformation("Signing keys loaded: {Keys}", signature);
        await previous.CancelAsync();
        previous.Dispose();
    }

    private SecurityKey ToSecurityKey(SigningKey row)
    {
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(master.Decrypt(Purpose, row.PrivateKeyEncrypted), out _);
        return new RsaSecurityKey(rsa) { KeyId = row.Kid };
    }

    private SigningKey NewKey(SigningKeyStatus status, DateTimeOffset now)
    {
        using var rsa = RSA.Create(3072);
        var kid = now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        var p = rsa.ExportParameters(false);
        var jwk = JsonSerializer.Serialize(new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid,
            n = Base64UrlEncoder.Encode(p.Modulus),
            e = Base64UrlEncoder.Encode(p.Exponent),
        });
        return new SigningKey
        {
            Kid = kid,
            Algorithm = "RS256",
            PrivateKeyEncrypted = master.Encrypt(Purpose, rsa.ExportPkcs8PrivateKey()),
            PublicJwk = jwk,
            Status = status,
            CreatedAt = now,
            ActivatedAt = status == SigningKeyStatus.Active ? now : null,
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _changed.Dispose();
        }
    }
}
