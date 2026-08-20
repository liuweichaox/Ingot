// 实现基础设施适配器 AgentRunSnapshotReader，满足应用层端口而不改变领域契约。

using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.Insight;

namespace Ingot.Platform.Infrastructure.AgentRuns;

public sealed class AgentRunSnapshotReader(IAgentRunStore runs) : IAgentRunSnapshotReader
{
    public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
        => runs.GetAsync(runId, ct);
}
