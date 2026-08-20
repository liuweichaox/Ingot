using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Ingot.Platform.Application.Identity;

namespace Ingot.Platform.Infrastructure.Identity;

/// <summary>
///     本地账户体系的组合入口。仅在 Authentication:Mode=Local（生产自托管默认）时由宿主调用。
///     API 只注册认证所需服务；首用户引导和周期维护分别归属 Migrator 与 Worker。
/// </summary>
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
        return services;
    }

    /// <summary>由独立 Worker 注册本地身份的周期维护，不在无状态 API 内运行。</summary>
    public static IServiceCollection AddIngotLocalIdentityMaintenance(this IServiceCollection services)
    {
        services.TryAddSingleton<ILocalUserStore, PostgresLocalUserStore>();
        services.AddHostedService<SessionPruneHostedService>();
        return services;
    }

    /// <summary>由 Migrator 在 schema 就绪后执行一次原子化初始管理员引导。</summary>
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
