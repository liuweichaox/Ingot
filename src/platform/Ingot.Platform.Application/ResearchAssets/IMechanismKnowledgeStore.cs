using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

public interface IMechanismKnowledgeStore
{
    Task<MechanismClaimVersion?> GetClaimAsync(Guid claimId, int? version = null, CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(Guid projectId, CancellationToken ct = default);
    Task<MechanismClaimVersion> SaveDraftAsync(MechanismClaimVersion value, CancellationToken ct = default);
    Task<bool> EvidenceExistsAsync(Guid projectId, MechanismClaimEvidence evidence, CancellationToken ct = default);
    Task<MechanismClaimVersion> AddReviewAsync(MechanismClaimReview review, string targetStatus, CancellationToken ct = default);
    Task<MechanismClaimConflict> AddConflictAsync(MechanismClaimConflict value, CancellationToken ct = default);
    Task<MechanismClaimConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct = default);
    Task<MechanismClaimConflict> ResolveConflictAsync(MechanismClaimConflict value, CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(Guid projectId, CancellationToken ct = default);
    Task SaveUsagesAsync(IReadOnlyList<MechanismClaimUsage> values, CancellationToken ct = default);
    Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(Guid projectId, CancellationToken ct = default);
    Task<bool> LifecycleEvidenceUsedAsync(Guid claimId, string referenceId, CancellationToken ct = default);
    Task<bool> LifecycleActorUsedAsync(Guid claimId, string userId, CancellationToken ct = default);
    Task<MechanismClaimVersion> TransitionAsync(MechanismClaimLifecycleDecision decision, CancellationToken ct = default);
    Task<bool> ExperimentResultValidatesClaimAsync(
        Guid projectId,
        MechanismClaimVersion claim,
        Guid validationHypothesisId,
        MechanismClaimEvidence evidence,
        string evaluationOutcome = "supports",
        CancellationToken ct = default);
}
