using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Text;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Auth;

public enum SignInStatus { Success, Failed }

/// <summary>
/// Outcome shown to the person: only success or one generic failure (PROMPT 0, rule 8). <see cref="FailureCode"/> is
/// for the sign-in log only and never reaches the page.
/// </summary>
public sealed record SignInOutcome(SignInStatus Status, User? User, Session? Session, bool MustChangePassword, string? FailureCode);

public sealed record SignInRequest(string UserName, string Password, string? Ip, string? UserAgent, string? ClientId = null, string? Resource = null);

/// <summary>
/// Password sign-in. Every path costs one PBKDF2 derivation (unknown name, locked, wrong password, right password),
/// so the answer time does not tell whether the account exists. Soft lockout is per (account, IP) with growing delay,
/// never a hard lock an attacker could put on someone else (PROMPT 6.1); a per-IP limit sits in front (rate limiter).
/// </summary>
public sealed class SignInService(BlueIdDbContext db, PasswordService passwords, SessionService sessions, TimeProvider clock)
{
    public const int FreeFailures = 5;
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>Every answer takes at least this long, so the extra lookups of an existing account do not show.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(400);

    public async Task<SignInOutcome> PasswordSignInAsync(SignInRequest r, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var outcome = await SignInCoreAsync(r, ct);
        var left = MinimumDuration - System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (left > TimeSpan.Zero)
        {
            await Task.Delay(left, ct);
        }

        return outcome;
    }

    private async Task<SignInOutcome> SignInCoreAsync(SignInRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        User? user = null;
        Tenant? tenant = null;
        if (Identifiers.TryNormalizeAddress(r.UserName, out var upn))
        {
            user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserPrincipalName == upn && u.DeletedAt == null, ct);
            if (user is not null)
            {
                tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
            }
        }

        var lockKey = user is null ? null : $"u:{user.Id:N}|ip:{r.Ip ?? "-"}";
        var lockRow = lockKey is null ? null : await db.Lockouts.FirstOrDefaultAsync(l => l.Key == lockKey, ct);
        var locked = lockRow?.Until is { } until && until > now;

        var current = user is null || locked ? null : await passwords.CurrentAsync(user.Id, ct);
        // Unknown user and locked account still pay for one derivation (VerifyAsync uses the dummy hash when current is null).
        var passwordOk = await passwords.VerifyAsync(user ?? new User(), current, r.Password, ct);

        string? failure = null;
        if (user is null)
        {
            failure = "unknown_user";
        }
        else if (locked)
        {
            failure = "locked";
        }
        else if (!passwordOk)
        {
            failure = "bad_password";
        }
        else if (user.Status != UserStatus.Active)
        {
            failure = "user_disabled";
        }
        else if (tenant is null || tenant.Status is not (TenantStatus.Active or TenantStatus.Trial))
        {
            failure = "tenant_inactive";
        }

        if (failure is not null)
        {
            if (lockKey is not null && failure is "bad_password")
            {
                await RecordFailureAsync(lockKey, user!.TenantId, lockRow, now, ct);
            }

            await LogAsync(r, user, tenant, Domain.SignInResult.Failure, failure, now, ct);
            return new SignInOutcome(SignInStatus.Failed, null, null, false, failure);
        }

        if (lockRow is not null)
        {
            db.Lockouts.Remove(lockRow);
        }

        user!.LastSignInAt = now;
        await db.SaveChangesAsync(ct);
        var session = await sessions.CreateAsync(user, ["pwd"], r.Ip, r.UserAgent, ct);
        await LogAsync(r, user, tenant, Domain.SignInResult.Success, null, now, ct);
        return new SignInOutcome(SignInStatus.Success, user, session, current!.MustChange, null);
    }

    /// <summary>Delay after the free failures: 2^(n-5) seconds, capped at 15 minutes.</summary>
    public static TimeSpan DelayAfter(int failures) =>
        failures < FreeFailures ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(MaxDelay.TotalSeconds, Math.Pow(2, Math.Min(20, failures - FreeFailures))));

    private async Task RecordFailureAsync(string key, Guid tenantId, Lockout? row, DateTimeOffset now, CancellationToken ct)
    {
        // Atomic increment: parallel wrong guesses must all count.
        var failures = await db.Database.SqlQuery<int>($"""
            INSERT INTO auth.lockouts (key, tenant_id, failures, until, updated_at) VALUES ({key}, {tenantId}, 1, NULL, {now})
            ON CONFLICT (key) DO UPDATE SET failures = auth.lockouts.failures + 1, updated_at = {now}
            RETURNING failures AS value
            """).ToListAsync(ct);
        var delay = DelayAfter(failures[0]);
        if (delay > TimeSpan.Zero)
        {
            await db.Lockouts.Where(l => l.Key == key).ExecuteUpdateAsync(l => l.SetProperty(x => x.Until, now + delay), ct);
        }

        if (row is not null)
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    private async Task LogAsync(SignInRequest r, User? user, Tenant? tenant, Domain.SignInResult result, string? failure, DateTimeOffset now, CancellationToken ct)
    {
        // Attempts for unknown names are logged under the platform tenant so every row has a tenant_id (as in BlueMail D4).
        var tenantId = tenant?.Id ?? await db.Tenants.IgnoreQueryFilters().Where(t => t.Type == TenantType.Platform).Select(t => t.Id).FirstOrDefaultAsync(ct);
        db.SignInLogs.Add(new SignInLog
        {
            TenantId = tenantId,
            UserId = user?.Id,
            UpnAttemptedHash = Identifiers.HashForLog(r.UserName),
            ClientId = r.ClientId,
            Resource = r.Resource,
            Ip = r.Ip,
            UserAgent = r.UserAgent is { Length: > 512 } ua ? ua[..512] : r.UserAgent,
            Result = result,
            FailureCode = failure,
            OccurredAt = now,
        });
        await db.SaveChangesAsync(ct);
    }
}
