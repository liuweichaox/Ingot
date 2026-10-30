// 定义检验复核持久化端口及其按站点筛选能力。
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>提供检验复核记录和审计事件的持久化查询入口。</summary>
public interface IInspectionReviewStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionReviewResult> CreateAsync(
        CreateInspectionReviewRequest request,
        string executionId,
        string reviewedBy,
        CancellationToken ct = default);

    Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionReview>> QueryAsync(
        Guid? inspectionRecordId,
        string? executionId,
        int limit,
        string? siteId = null,
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
