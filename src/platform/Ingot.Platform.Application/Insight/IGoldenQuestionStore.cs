using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Insight;

/// <summary>保存用于评估 Agent 分析质量的黄金问题和评测结果。</summary>
public interface IGoldenQuestionStore
{
    Task<IReadOnlyList<GoldenQuestionCase>> ListAsync(string? status, CancellationToken ct = default);
    Task<GoldenQuestionCase?> GetAsync(Guid caseId, int version, CancellationToken ct = default);
    Task<GoldenQuestionCase> SaveAsync(GoldenQuestionCase value, CancellationToken ct = default);
    Task SaveEvaluationAsync(
        GoldenQuestionEvaluation value,
        AgentRunSnapshot sourceRun,
        CancellationToken ct = default);
    Task<IReadOnlyList<GoldenQuestionEvaluation>> ListEvaluationsAsync(
        Guid? caseId,
        int limit,
        CancellationToken ct = default);
}
