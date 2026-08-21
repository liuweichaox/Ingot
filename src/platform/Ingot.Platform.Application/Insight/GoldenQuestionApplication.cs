using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Insight;

public interface IAgentRunSnapshotReader
{
    Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default);
}

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
        CancellationToken ct = default)
    {
        var run = await agentRuns.GetAsync(agentRunId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("指定 Agent 运行不存在。", nameof(agentRunId));
        var result = evaluator.Evaluate(goldenCase, run);
        await questions.SaveEvaluationAsync(result, run, ct).ConfigureAwait(false);
        return result;
    }
}
