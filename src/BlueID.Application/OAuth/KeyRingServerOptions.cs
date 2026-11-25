using BlueID.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace BlueID.Application.OAuth;

/// <summary>
/// Feeds OpenIddict's signing and encryption credentials from the key ring. OpenIddict reads its options through
/// IOptionsMonitor, so when the key ring changes (rotation on any node) the options are rebuilt: new tokens are signed with
/// the new ACTIVE key and JWKS lists the new set, with no restart (lesson 5).
/// </summary>
public sealed class KeyRingServerOptions(KeyRing ring, MasterKey master)
    : IConfigureOptions<OpenIddictServerOptions>, IOptionsChangeTokenSource<OpenIddictServerOptions>
{
    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => ring.GetChangeToken();

    public void Configure(OpenIddictServerOptions options)
    {
        // ACTIVE first (it signs), then NEXT and RETIRING (published in JWKS, accepted when validating).
        foreach (var key in ring.Keys)
        {
            options.SigningCredentials.Add(new SigningCredentials(key.Key, SecurityAlgorithms.RsaSha256));
        }

        // Authorization codes, refresh tokens and other server-only tokens are encrypted with a key derived from the master key.
        options.EncryptionCredentials.Add(new EncryptingCredentials(
            new SymmetricSecurityKey(master.Derive("openiddict-encryption", 32)) { KeyId = "enc-1" },
            SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
    }
}

/// <summary>Reloads the key ring on every node and runs the scheduled rotation (one node at a time, advisory lock).</summary>
public sealed class KeyRingRefresher(KeyRing ring, Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes, ILogger<KeyRingRefresher> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(KeyRing.RefreshInterval);
        var lastRotationCheck = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ring.ReloadAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastRotationCheck > TimeSpan.FromHours(1))
                {
                    lastRotationCheck = DateTimeOffset.UtcNow;
                    await ring.RotateAsync(stoppingToken);
                    // Hourly housekeeping of single-use records whose token has expired anyway.
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlueIdDbContext>();
                    await db.UsedTokens.Where(u => u.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the keys already loaded; the next tick tries again.
                log.LogError(ex, "Key ring refresh failed");
            }
        }
    }
}
