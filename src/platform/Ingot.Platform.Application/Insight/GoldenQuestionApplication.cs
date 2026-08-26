// 编排黄金问题持久化和基于当前站点授权的 Agent 运行评测。
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Insight;

/// <summary>读取已持久化 Agent 运行快照，供受控评测使用。</summary>
public interface IAgentRunSnapshotReader
{
    Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default);
}

/// <summary>编排黄金问题维护和经站点授权的 Agent 运行评测。</summary>
public sealed class GoldenQuestionApplication(
    IGoldenQuestionStore questions,
    IAgentRunSnapshotReader agentRuns,
    GoldenQuestionEvaluator evaluator)
{
    public Task<IReadOnlyList<GoldenQuestionCase>> ListAsync(string? status, CancellationToken ct = default)
        => questions.ListAsync(status, ct);
    public Task<GoldenQuestionCase?> GetAsync(Guid id, int version, CancellationToken ct = default)
        => questions.GetAsync(id, version, ct);
    public Task<GoldenQuestionCase> SaveAsync(GoldenQuestionCase value, CancellationToken ct = default)
        => questions.SaveAsync(value, ct);
    public Task<IReadOnlyList<GoldenQuestionEvaluation>> ListEvaluationsAsync(
        Guid? id, int limit, CancellationToken ct = default)
        => questions.ListEvaluationsAsync(id, limit, ct);

    public async Task<GoldenQuestionEvaluation> EvaluateAsync(
        GoldenQuestionCase goldenCase,
        string agentRunId,
        string requesterUserId,
        bool requesterAllowAllSites,
        IReadOnlySet<string> requesterSiteIds,
        CancellationToken ct = default)
    {
        var run = await agentRuns.GetAsync(agentRunId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("指定 Agent 运行不存在。", nameof(agentRunId));
        EnsureRunAccess(run, requesterUserId, requesterAllowAllSites, requesterSiteIds);
        var result = evaluator.Evaluate(goldenCase, run);
        await questions.SaveEvaluationAsync(result, run, ct).ConfigureAwait(false);
        return result;
    }

    private static void EnsureRunAccess(
        AgentRunSnapshot run,
        string requesterUserId,
        bool requesterAllowAllSites,
        IReadOnlySet<string> requesterSiteIds)
    {
        var capturedScope = run.AccessScope;
        if (capturedScope is null ||
            capturedScope.Version != AgentRunAccessScopeSnapshot.CurrentVersion ||
            (!requesterAllowAllSites &&
             (!string.Equals(run.UserId, requesterUserId, StringComparison.OrdinalIgnoreCase) ||
              capturedScope.AllowAllSites ||
              capturedScope.SiteIds.Count == 0 ||
              capturedScope.SiteIds.Any(siteId => !requesterSiteIds.Contains(siteId)))))
            throw new UnauthorizedAccessException("当前用户无权使用该 Agent 运行进行评测。");
    }
}
