using System.Net.Http.Headers;
using BlueID.Domain;
using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BlueID.Application.OAuth;

/// <summary>
/// OIDC Back-Channel Logout 1.0 (docs/05 6). When a BlueID session ends, every application that obtained tokens through
/// it gets a logout token. Rows are written in the caller's transaction; delivery is asynchronous and retried.
/// </summary>
public sealed class BackchannelLogoutQueue(BlueIdDbContext db, TimeProvider clock)
{
    /// <summary>Records that a client got tokens through a session (called on every successful authorization).</summary>
    public async Task RecordClientAsync(Guid sessionId, Guid tenantId, Guid userId, string clientId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO auth.session_clients (session_id, client_id, tenant_id, user_id, first_at)
            VALUES ({sessionId}, {clientId}, {tenantId}, {userId}, {clock.GetUtcNow()})
            ON CONFLICT (session_id, client_id) DO NOTHING
            """, ct);
    }

    /// <summary>Queues logout tokens for every client of these sessions that registered a back-channel logout URI.</summary>
    public async Task EnqueueAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var ids = sessionIds.ToArray();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO auth.logout_deliveries (session_id, user_id, tenant_id, client_id, uri, status, attempts, next_attempt_at, created_at)
            SELECT sc.session_id, sc.user_id, sc.tenant_id, sc.client_id, a.backchannel_logout_uri, 'PENDING', 0, {now}, {now}
            FROM auth.session_clients sc
            JOIN apps.applications a ON a.client_id = sc.client_id AND a.deleted_at IS NULL
            WHERE sc.session_id = ANY({ids}) AND a.backchannel_logout_uri IS NOT NULL
            ON CONFLICT (session_id, client_id) DO NOTHING
            """, ct);
    }
}

/// <summary>
/// Delivers queued logout tokens. Rows are claimed with FOR UPDATE SKIP LOCKED, so several nodes can run it without
/// sending twice. Back-off 1 s, 2 s, 4 s … capped at 10 minutes, for 72 hours (same schedule as events, docs/05 7.3).
/// </summary>
public sealed class BackchannelLogoutSender(IServiceScopeFactory scopes, IHttpClientFactory http, KeyRing keys, BidSettings settings,
    TimeProvider clock, ILogger<BackchannelLogoutSender> log) : BackgroundService
{
    public const string HttpClientName = "backchannel-logout";
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(72);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (await RunOnceAsync(stoppingToken) > 0)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Back-channel logout delivery loop failed");
            }
        }
    }

    /// <summary>Claims and sends one batch; returns how many rows were processed (tests call it directly).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueIdDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var batch = await db.LogoutDeliveries
            .FromSql($"SELECT * FROM auth.logout_deliveries WHERE status = 'PENDING' AND next_attempt_at <= {now} ORDER BY id LIMIT 20 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        foreach (var d in batch)
        {
            var error = await SendAsync(d, ct);
            d.Attempts++;
            if (error is null)
            {
                d.Status = DeliveryStatus.Delivered;
                d.DeliveredAt = clock.GetUtcNow();
                d.LastError = null;
            }
            else
            {
                d.LastError = error.Length > 500 ? error[..500] : error;
                var delay = TimeSpan.FromSeconds(Math.Min(600, Math.Pow(2, Math.Min(10, d.Attempts - 1))));
                if (now + delay - d.CreatedAt > GiveUpAfter)
                {
                    d.Status = DeliveryStatus.Failed;
                    log.LogWarning("Back-channel logout to {ClientId} given up after {Attempts} attempts: {Error}", d.ClientId, d.Attempts, d.LastError);
                }
                else
                {
                    d.NextAttemptAt = clock.GetUtcNow() + delay;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private async Task<string?> SendAsync(LogoutDelivery d, CancellationToken ct)
    {
        try
        {
            var token = CreateLogoutToken(d);
            using var client = http.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("logout_token", token)]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var res = await client.PostAsync(d.Uri, content, ct);
            return res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    /// <summary>Logout token: iss, aud, iat, exp (2 min), jti, sub, sid, tid, events; never a nonce (spec 2.4).</summary>
    public string CreateLogoutToken(LogoutDelivery d)
    {
        var active = keys.Active;
        var now = clock.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer + "/",
            Audience = d.ClientId,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(2).UtcDateTime,
            TokenType = "logout+jwt",
            SigningCredentials = new SigningCredentials(active.Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["sub"] = d.UserId.ToString("D"),
                ["sid"] = d.SessionId.ToString("D"),
                ["tid"] = d.TenantId.ToString("D"),
                ["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() },
            },
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
