using BlueID.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlueID.Infrastructure.Persistence;

/// <summary>For <c>dotnet ef migrations add</c> only; the connection is never opened.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BlueIdDbContext>
{
    public BlueIdDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<BlueIdDbContext>();
        InfrastructureServices.ConfigureDb(o, "Host=localhost;Database=blueid;Username=design;Password=design", 1);
        return new BlueIdDbContext(o.Options, new TenantContext());
    }
}
