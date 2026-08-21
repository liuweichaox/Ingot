using Ingot.Platform.Application.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Identity;

public static class LocalIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIngotLocalIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LocalAuthOptions>(configuration.GetSection("Authentication:Local"));
        services.AddSingleton<LocalPasswordHasher>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<ILocalUserStore, PostgresLocalUserStore>();
        services.AddSingleton<LocalIdentityApplication>();
        return services;
    }

    public static IServiceCollection AddIngotLocalIdentityMaintenance(this IServiceCollection services)
    {
        services.TryAddSingleton<ILocalUserStore, PostgresLocalUserStore>();
        services.AddHostedService<SessionPruneHostedService>();
        return services;
    }

    public static IServiceCollection AddIngotLocalIdentityBootstrap(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LocalAuthOptions>(configuration.GetSection("Authentication:Local"));
        services.TryAddSingleton(configuration);
        services.TryAddSingleton<NpgsqlDataSource>(_ => PostgresDataSourceFactory.Create(configuration));
        services.AddSingleton<LocalPasswordHasher>();
        services.AddSingleton<LocalAdminBootstrapper>();
        return services;
    }
}
