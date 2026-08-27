// 提供按用户成员关系和站点范围过滤的研发项目只读查询。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>提供按成员和站点范围过滤的研发项目查询用例。</summary>
public sealed class ProcessResearchQueries(IProcessResearchStore research)
{
    public Task<ResearchProject?> GetProjectAsync(Guid id, CancellationToken ct = default)
        => research.GetProjectAsync(id, ct);
    public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
        string userId,
        bool includeAll,
        IReadOnlyCollection<string>? siteIds,
        int limit,
        int offset,
        CancellationToken ct = default)
        => research.ListProjectsAsync(userId, includeAll, siteIds, limit, offset, ct);
    public Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
        Guid id, CancellationToken ct = default)
        => research.GetValidationPreregistrationAsync(id, ct);
    public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
        Guid id, CancellationToken ct = default)
        => research.GetRecipeRecommendationAsync(id, ct);
    public Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListRecipeRecommendationsPageAsync(projectId, cursor, limit, ct);
    public Task<ResearchExperiment?> GetExperimentAsync(Guid id, CancellationToken ct = default)
        => research.GetExperimentAsync(id, ct);
    public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(Guid projectId, CancellationToken ct = default)
        => research.ListExperimentsAsync(projectId, ct);
    public Task<ResearchPage<ResearchExperiment>> ListExperimentsPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListExperimentsPageAsync(projectId, cursor, limit, ct);
    public Task<ResearchPage<ResearchExperimentResult>> ListExperimentResultsPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListExperimentResultsPageAsync(projectId, cursor, limit, ct);
    public Task<ResearchShadowRecommendation?> GetShadowRecommendationAsync(Guid id, CancellationToken ct = default)
        => research.GetShadowRecommendationAsync(id, ct);
    public Task<ResearchPage<ResearchShadowRecommendation>> ListShadowRecommendationsPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListShadowRecommendationsPageAsync(projectId, cursor, limit, ct);
    public Task<ResearchHistoricalReplayReport?> GetHistoricalReplayReportAsync(Guid id, CancellationToken ct = default)
        => research.GetHistoricalReplayReportAsync(id, ct);
    public Task<ResearchPage<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListHistoricalReplayReportsPageAsync(projectId, cursor, limit, ct);
    public Task<ResearchRollbackDrill?> GetRollbackDrillAsync(Guid id, CancellationToken ct = default)
        => research.GetRollbackDrillAsync(id, ct);
    public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(Guid id, CancellationToken ct = default)
        => research.GetOperatingRegionAsync(id, ct);
    public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
        Guid projectId, CancellationToken ct = default)
        => research.ListOperatingRegionsAsync(projectId, ct);
    public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(Guid id, CancellationToken ct = default)
        => research.GetKnowledgeClaimAsync(id, ct);
    public Task<ResearchTransferAssessment?> GetTransferAssessmentAsync(Guid id, CancellationToken ct = default)
        => research.GetTransferAssessmentAsync(id, ct);
    public Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
        Guid projectId, string? cursor, int limit, CancellationToken ct = default)
        => research.ListAuditEntriesPageAsync(projectId, cursor, limit, ct);
}
