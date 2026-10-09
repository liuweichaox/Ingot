// 定义下一配方建议、工程师决定、实际运行关联和结果冻结的持久化端口。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>按站点和配方筛选建议；站点集合为空表示调用方可访问全部站点。</summary>
public sealed record RecipeRecommendationFilter(
    IReadOnlyCollection<string>? SiteCodes,
    string? ProcessSpecificationId = null);

/// <summary>建议与其证据链只追加写入；决定、关联和结果一经保存不能覆盖。</summary>
public interface IProcessResearchStore
{
    Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
        string siteCode,
        string processSpecificationId,
        string inputHash,
        CancellationToken ct = default);
    Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
        RecipeRecommendationFilter filter,
        string? cursor,
        int limit,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendation> CreateRecipeRecommendationAsync(
        ResearchRecipeRecommendation value,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
        Guid decisionId,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
        Guid recommendationId,
        string recommendationKey,
        CancellationToken ct = default);
    /// <summary>返回同一站点和配方下已接受或已修改、但尚未冻结结果的决定。</summary>
    Task<IReadOnlyList<ResearchRecipeRecommendationDecision>> ListPendingRecipeRecommendationDecisionsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
        ResearchRecipeRecommendationDecision value,
        string? actualExecutionKey,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> LinkRecipeRecommendationDecisionExecutionTransactionAsync(
        Guid decisionId,
        string actualExecutionKey,
        string linkedBy,
        CancellationToken ct = default);
    Task<ResearchRecipeRecommendationDecision> AttachRecipeRecommendationOutcomeTransactionAsync(
        Guid decisionId,
        ResearchRecipeRecommendationOutcome outcome,
        string materializedBy,
        CancellationToken ct = default);
}
