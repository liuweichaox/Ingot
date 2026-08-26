// 验证黄金问题评测读取持久化 Agent run 时复用捕获站点范围并拒绝旁路越权。

using Ingot.Contracts.Agents;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class GoldenQuestionApplicationScopeTests
{
    [Fact]
    public async Task EvaluateAsync_DeniesOwner_WhenCurrentSitesDoNotCoverCapturedScope()
    {
        var fixture = Fixture(Run("operator", Scope("SITE-A")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Application.EvaluateAsync(
            GoldenCase(),
            fixture.Run.RunId,
            "operator",
            requesterAllowAllSites: false,
            Sites("SITE-B")));

        Assert.Empty(fixture.Store.SavedEvaluations);
    }

    [Fact]
    public async Task EvaluateAsync_DeniesNonOwner_EvenWhenSiteScopeMatches()
    {
        var fixture = Fixture(Run("owner", Scope("SITE-A")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Application.EvaluateAsync(
            GoldenCase(),
            fixture.Run.RunId,
            "other-user",
            requesterAllowAllSites: false,
            Sites("SITE-A")));

        Assert.Empty(fixture.Store.SavedEvaluations);
    }

    [Fact]
    public async Task EvaluateAsync_DeniesLegacyRunWithoutCapturedScope()
    {
        var fixture = Fixture(Run("operator", accessScope: null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Application.EvaluateAsync(
            GoldenCase(),
            fixture.Run.RunId,
            "operator",
            requesterAllowAllSites: false,
            Sites("SITE-A")));

        Assert.Empty(fixture.Store.SavedEvaluations);
    }

    [Fact]
    public async Task EvaluateAsync_AllowsOwner_WhenCurrentScopeCoversCapturedScope()
    {
        var fixture = Fixture(Run("operator", Scope("SITE-A")));
        var goldenCase = GoldenCase();

        var result = await fixture.Application.EvaluateAsync(
            goldenCase,
            fixture.Run.RunId,
            "operator",
            requesterAllowAllSites: false,
            Sites("SITE-A", "SITE-B"));

        Assert.Equal(fixture.Run.RunId, result.AgentRunId);
        var saved = Assert.Single(fixture.Store.SavedEvaluations);
        Assert.Equal(result.EvaluationId, saved.Evaluation.EvaluationId);
        Assert.Same(fixture.Run, saved.SourceRun);
    }

    private static TestFixture Fixture(AgentRunSnapshot run)
    {
        var store = new RecordingGoldenQuestionStore();
        return new TestFixture(
            run,
            store,
            new GoldenQuestionApplication(store, new SingleRunReader(run), new GoldenQuestionEvaluator()));
    }

    private static IReadOnlySet<string> Sites(params string[] siteIds)
        => new HashSet<string>(siteIds, StringComparer.OrdinalIgnoreCase);

    private static AgentRunAccessScopeSnapshot Scope(params string[] siteIds) => new()
    {
        SiteIds = siteIds
    };

    private static GoldenQuestionCase GoldenCase() => new()
    {
        CaseId = Guid.CreateVersion7(),
        Version = 1,
        Name = "站点范围评测",
        Question = "核对站点数据",
        Status = GoldenQuestionStatuses.Reviewed,
        ReviewedBy = "engineer",
        ReviewedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AgentRunSnapshot Run(
        string ownerUserId,
        AgentRunAccessScopeSnapshot? accessScope) => new()
    {
        RunId = "run-1",
        UserId = ownerUserId,
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Question = "核对站点数据",
        AccessScope = accessScope,
        Mode = "quick",
        Status = AgentRunStatuses.Completed,
        ModelProvider = "test",
        Model = "test-model",
        PromptVersion = "test-prompt",
        ToolsetVersion = "test-tools",
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Answer = new AnalysisAnswer { Summary = "完成" },
        Usage = new AgentUsageSummary()
    };

    private sealed record TestFixture(
        AgentRunSnapshot Run,
        RecordingGoldenQuestionStore Store,
        GoldenQuestionApplication Application);

    private sealed class SingleRunReader(AgentRunSnapshot run) : IAgentRunSnapshotReader
    {
        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<AgentRunSnapshot?>(run.RunId == runId ? run : null);
    }

    private sealed class RecordingGoldenQuestionStore : IGoldenQuestionStore
    {
        public List<(GoldenQuestionEvaluation Evaluation, AgentRunSnapshot SourceRun)> SavedEvaluations { get; } = [];

        public Task<IReadOnlyList<GoldenQuestionCase>> ListAsync(
            string? status,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GoldenQuestionCase>>([]);

        public Task<GoldenQuestionCase?> GetAsync(
            Guid caseId,
            int version,
            CancellationToken ct = default)
            => Task.FromResult<GoldenQuestionCase?>(null);

        public Task<GoldenQuestionCase> SaveAsync(
            GoldenQuestionCase value,
            CancellationToken ct = default)
            => Task.FromResult(value);

        public Task SaveEvaluationAsync(
            GoldenQuestionEvaluation value,
            AgentRunSnapshot sourceRun,
            CancellationToken ct = default)
        {
            SavedEvaluations.Add((value, sourceRun));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GoldenQuestionEvaluation>> ListEvaluationsAsync(
            Guid? caseId,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GoldenQuestionEvaluation>>([]);
    }
}
