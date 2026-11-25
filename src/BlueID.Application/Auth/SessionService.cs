using System.Text.Json;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Auth;

public sealed record SessionCheck(bool IsValid, Guid TenantId, Guid UserId, Guid SessionId);

/// <summary>Browser sessions of BlueID (auth.sessions). Every request with a session cookie is checked here.</summary>
public sealed class SessionService(BlueIdDbContext db, OAuth.BackchannelLogoutQueue backchannel, TimeProvider clock)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);

    public async Task<Session> CreateAsync(User user, IReadOnlyList<string> amr, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var s = new Session
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            Ip = ip,
            UserAgent = userAgent is { Length: > 512 } ua ? ua[..512] : userAgent,
            Amr = JsonSerializer.Serialize(amr),
            ExpiresAt = now + DefaultLifetime,
        };
        db.Sessions.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    /// <summary>
    /// Session not revoked or expired, user active, tenant active, security stamp unchanged. Same rule for every way
    /// in (lesson 8). Platform-level query: runs before the tenant of the request is known.
    /// </summary>
    public async Task<SessionCheck> ValidateAsync(Guid userId, Guid sessionId, string stamp, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var row = await (from s in db.Sessions.IgnoreQueryFilters()
                         join u in db.Users.IgnoreQueryFilters() on s.UserId equals u.Id
                         join t in db.Tenants.IgnoreQueryFilters() on u.TenantId equals t.Id
                         where s.Id == sessionId && s.UserId == userId
                         select new { s.TenantId, s.RevokedAt, s.ExpiresAt, s.LastSeenAt, s.DeletedAt, u.Status, u.SecurityStamp, UserDeleted = u.DeletedAt, TenantStatus = t.Status })
            .AsNoTracking().FirstOrDefaultAsync(ct);
        var valid = row is not null && row.RevokedAt is null && row.DeletedAt is null && row.ExpiresAt > now && row.UserDeleted is null &&
                    row.Status == UserStatus.Active && row.TenantStatus is TenantStatus.Active or TenantStatus.Trial &&
                    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(row.SecurityStamp), System.Text.Encoding.UTF8.GetBytes(stamp));
        if (valid && now - row!.LastSeenAt > TouchEvery)
        {
            await db.Sessions.IgnoreQueryFilters().Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, now + DefaultLifetime), ct);
        }

        return new SessionCheck(valid, row?.TenantId ?? Guid.Empty, userId, sessionId);
    }

    /// <summary>
    /// Ends one session (sign-out) and queues back-channel logout for the applications that used it. Revocation by an
    /// administrator goes through IDirectoryWriter (it also raises an event).
    /// </summary>
    public async Task EndAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var n = await db.Sessions.IgnoreQueryFilters().Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()).SetProperty(x => x.RevokeReason, reason), ct);
        if (n > 0)
        {
            await backchannel.EnqueueAsync([sessionId], ct);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }
    }
}
