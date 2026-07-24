namespace Ingot.Platform.Infrastructure.Identity;

/// <summary>
///     本地账户体系的组合入口。仅在 Authentication:Mode=Local（生产自托管默认）时由宿主调用。
///     必须在 AddIngotPlatformInfrastructure 之后调用，以保证播种服务晚于数据库迁移运行。
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
        // 播种服务自身判定 Authentication:Mode，仅在 Local 模式建初始管理员；因此可无条件注册。
        services.AddHostedService<AdminSeederHostedService>();
        services.AddHostedService<SessionPruneHostedService>();
        return services;
    }
}
