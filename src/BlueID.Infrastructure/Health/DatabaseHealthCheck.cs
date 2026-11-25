using BlueID.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlueID.Infrastructure.Health;

/// <summary>/health/ready: the database answers (a node that cannot reach PostgreSQL leaves the load balancer).</summary>
public sealed class DatabaseHealthCheck(BlueIdDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("database unreachable");
}
