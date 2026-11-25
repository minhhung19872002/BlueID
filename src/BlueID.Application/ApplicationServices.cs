using BlueID.Application.Auth;
using BlueID.Application.Directory;
using BlueID.Application.Seeding;
using BlueID.Application.Security;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BlueID.Application;

public static class ApplicationServices
{
    /// <summary>Services every BlueID process needs (Web, Api, Worker).</summary>
    public static IServiceCollection AddBlueIdApplication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMediatR(c => c.RegisterServicesFromAssembly(typeof(ApplicationServices).Assembly));
        services.AddValidatorsFromAssembly(typeof(ApplicationServices).Assembly, includeInternalTypes: true);
        services.AddScoped<IDirectoryWriter, DirectoryWriter>();
        services.AddScoped<PasswordService>();
        services.AddScoped<SessionService>();
        services.AddScoped<SignInService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<SampleDataSeeder>();
        return services;
    }
}
