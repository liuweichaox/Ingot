// 将 Chat 应用端口适配到实现中立的 Agent 运行时。
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.Chat;

namespace Ingot.Platform.Infrastructure.AgentRuns;

public sealed class AgentChatRunGateway(IAgentRuntime runtime) : IChatRunGateway
{
    public Task<AgentRunSnapshot> StartAsync(
        string userId,
        CreateChatRunRequest request,
        AgentRunAccessScopeSnapshot accessScope,
        CancellationToken ct = default)
        => runtime.StartAsync(
            ProductEntryPoints.Chat,
            userId,
            request,
            new AgentAccessScope
            {
                AllowAllSites = accessScope.AllowAllSites,
                SiteIds = accessScope.SiteIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            },
            ct);

    public Task<bool> DeleteConversationAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default)
        => runtime.DeleteConversationAsync(
            ProductEntryPoints.Chat,
            conversationId,
            userId,
            ct);
}
