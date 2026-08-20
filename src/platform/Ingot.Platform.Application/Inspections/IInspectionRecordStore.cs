using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>保存和查询与生产执行关联的正式检验记录。</summary>
public interface IInspectionRecordStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionRecordResult> CreateAsync(
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        CancellationToken ct = default);

    Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default);

    Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default);

    Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default);

    Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default);

    Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    Task<InspectionRecordPage> QueryPageAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    /// <summary>
    ///     返回指定生产过程执行的全部检测记录，供确定性分析使用；不受公共查询 API 的单页 Limit 限制。
    /// </summary>
    Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default);
}

public sealed record StoreInspectionRecordResult
{
    public required InspectionRecord Record { get; init; }

    public required bool Created { get; init; }

    public required bool PayloadConflict { get; init; }
}
