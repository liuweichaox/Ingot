namespace Ingot.Platform.Infrastructure.Migrations;

/// <summary>
///     启动期迁移入口：必须在 AddIngotPlatformInfrastructure 中最先注册，
///     以保证 schema 在所有 Store 初始化器与业务 HostedService 之前就绪。
///     迁移失败时抛出异常终止启动——宁可不启动，不带着未知 schema 运行。
/// </summary>
public sealed class MigrationHostedService(
    MigrationRunner runner,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "数据库迁移失败，服务终止启动。");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
