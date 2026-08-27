// 定义研发项目、假设、实验、审核与验证证据的持久化端口。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>保存完整研发工作流状态，并提供显式事务操作。</summary>
public interface IProcessResearchStore
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ResearchProject?> GetProjectByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
        string userId,
        bool includeAll,
        IReadOnlyCollection<string>? siteIds,
        int limit,
        int offset,
        CancellationToken ct = default);
    Task<ResearchProject> SaveProjectAsync(ResearchProject value, CancellationToken ct = default);

    Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
        Guid preregistrationId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchValidationPreregistration>> ListValidationPreregistrationsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchValidationPreregistration> CreateValidationPreregistrationAsync(
        ResearchValidationPreregistration value,
        CancellationToken ct = default);
    Task<ResearchValidationPreregistration> ReviewValidationPreregistrationAsync(
        ResearchValidationPreregistration value,
        CancellationToken ct = default);

    Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default);

    Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
        Guid projectId,
        string inputHash,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchRecipeRecommendation>> ListRecipeRecommendationsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendation> CreateRecipeRecommendationTransactionAsync(
        ResearchRecipeRecommendation value,
        ResearchAuditEntry audit,
        CancellationToken ct = default);

    Task<ResearchExperiment?> GetExperimentAsync(Guid experimentId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchPage<ResearchExperiment>> ListExperimentsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);

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
    Task<ResearchPage<ResearchShadowRecommendation>> ListShadowRecommendationsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
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
    Task<ResearchPage<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
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
    Task<ResearchPage<ResearchExperimentResult>> ListExperimentResultsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default);
    Task<ResearchExperimentResult> SaveExperimentResultAsync(
        ResearchExperimentResult value,
        CancellationToken ct = default);
    Task<ResearchExperimentResult> SaveExperimentResultTransactionAsync(
        ResearchExperimentResult result,
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);

    Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default);
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
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchTransferAssessment>> ListTransferAssessmentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchTransferAssessment> CreateTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default);
    Task<ResearchTransferAssessment> ReviewTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default);

    Task AddAuditEntryAsync(ResearchAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default);
}
