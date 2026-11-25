using System.Globalization;
using BlueID.Domain;
using BlueID.Infrastructure.Audit;
using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Persistence;
using BlueID.Infrastructure.Security;
using BlueID.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Formatting.Compact;

namespace BlueID.Infrastructure;

public static class InfrastructureServices
{
    /// <summary>Server processes compute, compare and format in InvariantCulture (PROMPT 0.2); only output is localised.</summary>
    public static void UseInvariantProcessCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    public static IServiceCollection AddBlueIdInfrastructure(this IServiceCollection services, BidSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<BlueIdDbContext>(o => ConfigureDb(o, settings.DbConnection, settings.DbMaxPool));
        services.AddScoped<AuditWriter>();
        services.AddScoped<AuditChainVerifier>();
        services.AddSingleton<IPasswordHasher<User>, Pbkdf2PasswordHasher>();
        return services;
    }

    public static void ConfigureDb(DbContextOptionsBuilder o, string connection, int maxPool)
    {
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = maxPool };
        o.UseNpgsql(cs.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations", "public"))
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>JSON logs (Serilog). Passwords, OTPs, tokens and secrets are never passed to the logger.</summary>
    public static void AddBlueIdLogging(this WebApplicationBuilder builder, BidSettings settings, string process)
    {
        builder.Services.AddSerilog((_, lc) => lc
            .ReadFrom.Configuration(builder.Configuration)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("process", process)
            .WriteTo.Console(new CompactJsonFormatter()));
    }
}
