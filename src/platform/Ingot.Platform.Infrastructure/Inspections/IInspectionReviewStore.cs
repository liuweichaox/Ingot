using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionReviewStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionReviewResult> CreateAsync(
        CreateInspectionReviewRequest request,
        string operationRunId,
        string reviewedBy,
        CancellationToken ct = default);

    Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionReview>> QueryAsync(
        Guid? inspectionRecordId,
        string? operationRunId,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(
        IReadOnlyCollection<Guid> inspectionRecordIds,
        CancellationToken ct = default);

    Task LogAccessAsync(
        Guid? inspectionRecordId,
        Guid? attachmentId,
        string action,
        string actor,
        string? detail,
        CancellationToken ct = default);

    Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(
        Guid? inspectionRecordId,
        Guid? attachmentId,
        int limit,
        CancellationToken ct = default);
}
public sealed record StoreInspectionReviewResult
{
    public required InspectionReview Review { get; init; }

    public required bool Created { get; init; }

    public required bool PayloadConflict { get; init; }
}
