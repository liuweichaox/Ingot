// 定义研发项目、假设、审核与真实运行证据的持久化端口。
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
    Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
        Guid decisionId,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
        Guid recommendationId,
        string recommendationKey,
        CancellationToken ct = default);
    Task<ResearchPage<ResearchRecipeRecommendationDecision>> ListRecipeRecommendationDecisionsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
        ResearchRecipeRecommendationDecision value,
        string? actualExecutionKey,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> LinkRecipeRecommendationDecisionExecutionTransactionAsync(
        Guid decisionId,
        string actualExecutionKey,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> AttachRecipeRecommendationOutcomeTransactionAsync(
        Guid decisionId,
        ResearchRecipeRecommendationOutcome outcome,
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
