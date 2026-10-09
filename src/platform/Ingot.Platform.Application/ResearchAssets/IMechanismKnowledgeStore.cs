// 定义机理声明、冲突、生命周期和配方建议知识引用的持久化端口。
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>
/// 持久化按站点与配方隔离的版本化机理知识，并记录配方建议与真实运行结果的知识使用。
/// </summary>
public interface IMechanismKnowledgeStore
{
    Task<MechanismClaimVersion?> GetClaimAsync(Guid claimId, int? version = null, CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default);
    Task<MechanismClaimVersion> SaveDraftAsync(MechanismClaimVersion value, CancellationToken ct = default);
    /// <summary>证据必须来自同一站点；配方建议结果还必须来自同一配方。</summary>
    Task<bool> EvidenceExistsAsync(
        string siteCode,
        string processSpecificationId,
        MechanismClaimEvidence evidence,
        CancellationToken ct = default);
    Task<MechanismClaimVersion> AddReviewAsync(MechanismClaimReview review, string targetStatus, CancellationToken ct = default);
    Task<MechanismClaimConflict> AddConflictAsync(MechanismClaimConflict value, CancellationToken ct = default);
    Task<MechanismClaimConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct = default);
    Task<MechanismClaimConflict> ResolveConflictAsync(MechanismClaimConflict value, CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default);
    Task SaveRecipeRecommendationUsagesAsync(
        IReadOnlyList<MechanismClaimUsage> values,
        CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(Guid recommendationId, CancellationToken ct = default);
    Task<bool> LifecycleEvidenceUsedAsync(Guid claimId, string referenceId, CancellationToken ct = default);
    Task<bool> LifecycleActorUsedAsync(Guid claimId, string userId, CancellationToken ct = default);
    Task<MechanismClaimVersion> TransitionAsync(MechanismClaimLifecycleDecision decision, CancellationToken ct = default);
    /// <summary>结果必须来自同一站点和配方下已接受或修改的建议，并且已冻结为可用于优化的真实运行。</summary>
    Task<bool> RecipeRecommendationOutcomeSupportsClaimAsync(
        MechanismClaimVersion claim,
        MechanismClaimEvidence evidence,
        CancellationToken ct = default);
}
