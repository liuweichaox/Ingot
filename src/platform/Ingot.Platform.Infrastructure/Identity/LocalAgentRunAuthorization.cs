using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.Identity;
using Microsoft.Extensions.Hosting;

namespace Ingot.Platform.Infrastructure.Identity;

/// <summary>以本地账户的当前角色和站点范围复核持久 Agent 任务。</summary>
public sealed class LocalAgentRunAuthorization(
    ILocalUserStore users,
    IHostEnvironment environment) : IAgentRunAuthorization
{
    private static readonly string[] AgentRoles =
    [
        "quality.inspector",
        "quality.reviewer",
        "process.engineer",
        "platform.admin"
    ];

    public async Task<AgentAccessScope?> ResolveCurrentScopeAsync(
        string userId,
        AgentRunAccessScopeSnapshot capturedScope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capturedScope);

        // 开发环境的匿名演示身份不对应 users 表；生产环境对无法解析的主体一律拒绝。
        if (!Guid.TryParse(userId, out var userIdValue))
            return environment.IsDevelopment() ? Restore(capturedScope) : null;

        var user = await users.GetByIdAsync(userIdValue, ct).ConfigureAwait(false);
        if (user is null || user.Disabled ||
            !user.Roles.Any(role => AgentRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            return null;

        var administrator = user.Roles.Contains("platform.admin", StringComparer.OrdinalIgnoreCase);
        var currentSites = new HashSet<string>(
            user.SiteIds.Where(static siteId => !string.IsNullOrWhiteSpace(siteId)),
            StringComparer.OrdinalIgnoreCase);
        if (capturedScope.AllowAllSites)
            return administrator ? new AgentAccessScope { AllowAllSites = true } : null;

        var capturedSites = capturedScope.SiteIds
            .Where(static siteId => !string.IsNullOrWhiteSpace(siteId) && siteId != "*")
            .ToArray();
        if (!administrator && capturedSites.Any(siteId => !currentSites.Contains(siteId)))
            return null;

        // 即使后来被提升为管理员，也维持任务创建时的最小站点范围。
        return new AgentAccessScope
        {
            SiteIds = new HashSet<string>(capturedSites, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static AgentAccessScope Restore(AgentRunAccessScopeSnapshot capturedScope) => new()
    {
        AllowAllSites = capturedScope.AllowAllSites,
        SiteIds = new HashSet<string>(capturedScope.SiteIds, StringComparer.OrdinalIgnoreCase)
    };
}
