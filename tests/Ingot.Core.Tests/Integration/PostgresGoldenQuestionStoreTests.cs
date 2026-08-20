// 验证 PostgresGoldenQuestionStore 的真实基础设施集成、失败和恢复行为。

using Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.AgentRuns;
using Ingot.Platform.Infrastructure.Insight;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresGoldenQuestionStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ReviewedVersion_ShouldBeImmutableAndRetainEvaluation()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresGoldenQuestionStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var draft = new GoldenQuestionCase
        {
            CaseId = Guid.CreateVersion7(),
            Version = 1,
            Name = "现场问题",
            Question = "为什么这次没有达到目标？",
            Status = GoldenQuestionStatuses.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveAsync(draft);
        var reviewed = draft with
        {
            Status = GoldenQuestionStatuses.Reviewed,
            ReviewedBy = "process-engineer",
            ReviewedAt = now,
            UpdatedAt = now.AddSeconds(1)
        };
        await store.SaveAsync(reviewed);

        var loaded = await store.GetAsync(draft.CaseId, 1);
        Assert.Equal(GoldenQuestionStatuses.Reviewed, loaded!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(reviewed with { Question = "被篡改的问题", UpdatedAt = now.AddMinutes(1) }));

        var sourceRun = new AgentRunSnapshot
        {
            RunId = $"run-{Guid.NewGuid():N}",
            UserId = "operator",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = reviewed.Question,
            Mode = "quick",
            Status = AgentRunStatuses.Completed,
            ModelProvider = "OpenAI",
            Model = "local-qwen",
            PromptVersion = "ingot-chat-v1",
            ToolsetVersion = "production-records-readonly-v2",
            CreatedAt = now,
            CompletedAt = now,
            Usage = new AgentUsageSummary()
        };
        var runStore = new PostgresAgentRunStore(postgres.DataSource);
        await runStore.CreateAsync(sourceRun);
        var evaluation = new GoldenQuestionEvaluation
        {
            EvaluationId = Guid.CreateVersion7(),
            CaseId = draft.CaseId,
            CaseVersion = 1,
            AgentRunId = sourceRun.RunId,
            Passed = true,
            ModelProvider = "OpenAI",
            Model = "local-qwen",
            PromptVersion = "ingot-chat-v1",
            ToolsetVersion = "production-records-readonly-v2",
            AgentRunSnapshotHash = GoldenQuestionEvaluator.SnapshotHash(sourceRun),
            EvaluatedAt = now
        };
        await store.SaveEvaluationAsync(evaluation, sourceRun);
        var evaluations = await store.ListEvaluationsAsync(draft.CaseId, 10);

        Assert.Contains(evaluations, item => item.EvaluationId == evaluation.EvaluationId);
        var deletion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runStore.DeleteAsync(sourceRun.RunId));
        Assert.Contains("正式评测证据", deletion.Message, StringComparison.Ordinal);
    }
}
