using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public interface IProcessResearchStore
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ResearchProject?> GetProjectByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
        string userId,
        bool includeAll,
        int limit,
        int offset,
        CancellationToken ct = default);
    Task<ResearchProject> SaveProjectAsync(ResearchProject value, CancellationToken ct = default);

    Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default);

    Task<ResearchExperiment?> GetExperimentAsync(Guid experimentId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default);
    async Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        var saved = await SaveExperimentAsync(updatedExperiment, ct).ConfigureAwait(false);
        await AddAuditEntryAsync(audit, ct).ConfigureAwait(false);
        return saved;
    }
    async Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        var saved = await SaveExperimentAsync(updatedExperiment, ct).ConfigureAwait(false);
        await AddAuditEntryAsync(audit, ct).ConfigureAwait(false);
        return saved;
    }

    Task<ResearchShadowRecommendation?> GetShadowRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default);
    Task<ResearchShadowRecommendation?> GetShadowRecommendationBySuggestionAsync(
        Guid experimentId,
        string suggestionExecutionKey,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchShadowRecommendation>> ListShadowRecommendationsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchShadowRecommendation> CreateShadowRecommendationAsync(
        ResearchShadowRecommendation value,
        CancellationToken ct = default);
    Task<ResearchShadowRecommendation> AttachShadowOutcomeAsync(
        ResearchShadowRecommendation value,
        CancellationToken ct = default);

    Task<ResearchHistoricalReplayReport?> GetHistoricalReplayReportAsync(
        Guid reportId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchHistoricalReplayReport> CreateHistoricalReplayReportAsync(
        ResearchHistoricalReplayReport value,
        CancellationToken ct = default);
    Task<ResearchHistoricalReplayReport> ReviewHistoricalReplayReportAsync(
        ResearchHistoricalReplayReport value,
        CancellationToken ct = default);
    Task<ResearchRollbackDrill?> GetRollbackDrillAsync(
        Guid drillId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchRollbackDrill>> ListRollbackDrillsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchRollbackDrill> CreateRollbackDrillAsync(
        ResearchRollbackDrill value,
        CancellationToken ct = default);
    Task<ResearchRollbackDrill> ReviewRollbackDrillAsync(
        ResearchRollbackDrill value,
        CancellationToken ct = default);
    Task<ResearchExperimentResult?> GetExperimentResultAsync(
        Guid resultId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchExperimentResult> SaveExperimentResultAsync(
        ResearchExperimentResult value,
        CancellationToken ct = default);
    async Task<ResearchExperimentResult> SaveExperimentResultTransactionAsync(
        ResearchExperimentResult result,
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        var saved = await SaveExperimentResultAsync(result, ct).ConfigureAwait(false);
        await SaveExperimentAsync(updatedExperiment, ct).ConfigureAwait(false);
        await AddAuditEntryAsync(audit, ct).ConfigureAwait(false);
        return saved;
    }

    Task<ResearchOperatingRegion?> GetOperatingRegionAsync(Guid operatingRegionId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
        ResearchOperatingRegion value,
        CancellationToken ct = default);

    Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(Guid claimId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        ResearchKnowledgeClaim value,
        CancellationToken ct = default);

    Task<ResearchTransferAssessment?> GetTransferAssessmentAsync(
        Guid assessmentId,
        CancellationToken ct = default)
        => Task.FromResult<ResearchTransferAssessment?>(null);
    Task<IReadOnlyList<ResearchTransferAssessment>> ListTransferAssessmentsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResearchTransferAssessment>>([]);
    Task<ResearchTransferAssessment> CreateTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前存储未实现迁移评估。");
    Task<ResearchTransferAssessment> ReviewTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前存储未实现迁移评估复核。");

    Task AddAuditEntryAsync(ResearchAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(
        Guid projectId,
        CancellationToken ct = default);
}

public sealed class ProcessResearchRuleException(string message) : InvalidOperationException(message);
