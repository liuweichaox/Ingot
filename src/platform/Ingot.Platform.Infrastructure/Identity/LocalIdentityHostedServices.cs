using System.Security.Cryptography;
using Ingot.Contracts.Identity;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.Identity;

/// <summary>
///     首次启动播种初始管理员：仅当用户表为空时执行。必须在迁移之后运行
///     数据库迁移由独立 Migrator 宿主在 API 启动前完成。
/// </summary>
public sealed class AdminSeederHostedService(
    ILocalUserStore store,
    LocalPasswordHasher hasher,
    IOptions<LocalAuthOptions> options,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AdminSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 开发环境用固定本地身份，不播种；仅生产 Local 模式建初始管理员。
        // OIDC 模式下身份由外部 IdP 提供，绝不建本地账户。
        var mode = configuration["Authentication:Mode"] ?? "Local";
        if (environment.IsDevelopment() || !string.Equals(mode, "Local", StringComparison.OrdinalIgnoreCase))
            return;
        if (await store.CountAsync(cancellationToken).ConfigureAwait(false) > 0)
            return;

        var username = string.IsNullOrWhiteSpace(options.Value.SeedAdminUsername)
            ? "admin"
            : options.Value.SeedAdminUsername.Trim();
        var password = options.Value.SeedAdminPassword;
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var now = DateTimeOffset.UtcNow;
        await store.CreateAsync(new UserAccount
        {
            UserId = Guid.CreateVersion7(),
            Username = username,
            UsernameLower = username.ToLowerInvariant(),
            DisplayName = "初始管理员",
            PasswordHash = hasher.Hash(password!),
            Roles = PlatformRoleNames.All,
            SiteIds = [],
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken).ConfigureAwait(false);

        if (generated)
            logger.LogCritical(
                "已创建初始管理员：用户名 {Username}，一次性口令 {Password}。请立即登录并修改口令；此口令仅本次显示。",
                username, password);
        else
            logger.LogInformation("已创建初始管理员：{Username}（口令来自配置）。", username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>周期性清理过期会话行。</summary>
public sealed class SessionPruneHostedService(
    ILocalUserStore store,
    ILogger<SessionPruneHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pruned = await store.PruneExpiredSessionsAsync(stoppingToken).ConfigureAwait(false);
                if (pruned > 0)
                    logger.LogInformation("已清理 {Count} 条过期会话。", pruned);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "清理过期会话失败，下个周期重试。");
            }
            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
