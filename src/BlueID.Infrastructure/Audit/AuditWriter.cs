using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Infrastructure.Audit;

public sealed record AuditEntry(
    string Action,
    AuditResult Result,
    Guid? TenantId = null,
    Guid? ActorId = null,
    string? ActorName = null,
    string? Ip = null,
    string? UserAgent = null,
    string? Entity = null,
    string? EntityId = null,
    object? OldValue = null,
    object? NewValue = null);

/// <summary>
/// Appends to audit.audit_logs keeping one hash chain (PROMPT 4). Writers serialise on a transaction-level advisory
/// lock, so two writers never link to the same previous row. Inside a caller's transaction the row commits or rolls
/// back with it.
/// </summary>
public sealed class AuditWriter(BlueIdDbContext db)
{
    private const long ChainLockKey = 0x426C75654944_4155; // "BlueID" + "AU"

    public async Task WriteAsync(AuditEntry e, CancellationToken ct = default)
    {
        var own = db.Database.CurrentTransaction is null;
        var tx = own ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [ChainLockKey], ct);
            var prev = await db.AuditLogs.OrderByDescending(x => x.Id).Select(x => x.Hash).FirstOrDefaultAsync(ct) ?? [];
            var row = new AuditLog
            {
                TenantId = e.TenantId,
                ActorId = e.ActorId,
                ActorName = e.ActorName,
                Ip = e.Ip,
                UserAgent = e.UserAgent is { Length: > 512 } ua ? ua[..512] : e.UserAgent,
                Action = e.Action,
                Entity = e.Entity,
                EntityId = e.EntityId,
                OldValue = e.OldValue is null ? null : JsonSerializer.Serialize(e.OldValue),
                NewValue = e.NewValue is null ? null : JsonSerializer.Serialize(e.NewValue),
                Result = e.Result,
                OccurredAt = TruncateToMicroseconds(DateTimeOffset.UtcNow),
                PrevHash = prev,
            };
            row.Hash = ComputeHash(row);
            db.AuditLogs.Add(row);
            await db.SaveChangesAsync(ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    /// <summary>PostgreSQL keeps microseconds; the hash covers exactly what is stored.</summary>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset t) => new(t.UtcTicks - (t.UtcTicks % 10), TimeSpan.Zero);

    /// <summary>SHA-256 over prev_hash and an unambiguous encoding of every column.</summary>
    public static byte[] ComputeHash(AuditLog r)
    {
        var sb = new StringBuilder();
        void Field(string? v) => sb.Append(v is null ? "␀" : v.Replace("\u001f", "\u001f\u001f", StringComparison.Ordinal)).Append('\u001f');
        Field(r.TenantId?.ToString("D"));
        Field(r.ActorId?.ToString("D"));
        Field(r.ActorName);
        Field(r.Ip);
        Field(r.UserAgent);
        Field(r.Action);
        Field(r.Entity);
        Field(r.EntityId);
        Field(CanonicalJson(r.OldValue));
        Field(CanonicalJson(r.NewValue));
        Field(r.Result.ToString());
        Field(r.OccurredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture));
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var input = new byte[r.PrevHash.Length + body.Length];
        r.PrevHash.CopyTo(input, 0);
        body.CopyTo(input, r.PrevHash.Length);
        return SHA256.HashData(input);
    }

    /// <summary>jsonb does not keep key order or whitespace: hash a canonical form (keys sorted ordinally, compact).</summary>
    public static string? CanonicalJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            Write(doc.RootElement, w);
        }

        return Encoding.UTF8.GetString(ms.ToArray());

        static void Write(JsonElement e, Utf8JsonWriter w)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        w.WritePropertyName(p.Name);
                        Write(p.Value, w);
                    }

                    w.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    w.WriteStartArray();
                    foreach (var item in e.EnumerateArray())
                    {
                        Write(item, w);
                    }

                    w.WriteEndArray();
                    break;
                default:
                    e.WriteTo(w);
                    break;
            }
        }
    }
}

public sealed record AuditVerification(bool Valid, long Checked, long? FirstBrokenId);

public sealed class AuditChainVerifier(BlueIdDbContext db)
{
    /// <summary>Walks the chain in id order; reports the first row whose link or content does not match.</summary>
    public async Task<AuditVerification> VerifyAsync(CancellationToken ct = default)
    {
        byte[] prev = [];
        long count = 0, lastId = 0;
        while (true)
        {
            var batch = await db.AuditLogs.AsNoTracking().Where(x => x.Id > lastId).OrderBy(x => x.Id).Take(1000).ToListAsync(ct);
            if (batch.Count == 0)
            {
                return new AuditVerification(true, count, null);
            }

            foreach (var row in batch)
            {
                if (!CryptographicOperations.FixedTimeEquals(row.PrevHash, prev) ||
                    !CryptographicOperations.FixedTimeEquals(AuditWriter.ComputeHash(row), row.Hash))
                {
                    return new AuditVerification(false, count, row.Id);
                }

                prev = row.Hash;
                lastId = row.Id;
                count++;
            }
        }
    }
}
