using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionRecordStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionRecordResult> CreateAsync(
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        CancellationToken ct = default);

    Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    /// <summary>
    ///     返回指定生产周期的全部检测记录，供确定性分析使用；不受公共查询 API 的单页 Limit 限制。
    /// </summary>
    Task<IReadOnlyList<InspectionRecord>> QueryAllByOperationRunIdsAsync(
        IReadOnlyCollection<string> operationRunIds,
        CancellationToken ct = default);
}

public sealed record StoreInspectionRecordResult
{
    public required InspectionRecord Record { get; init; }

    public required bool Created { get; init; }

    public required bool PayloadConflict { get; init; }
}

