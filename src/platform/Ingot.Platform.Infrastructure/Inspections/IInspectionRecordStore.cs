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

    Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default)
        => Task.FromResult<InspectionRecord?>(null);

    Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InspectionScope>>([]);

    Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default)
        => Task.FromResult<InspectionScope?>(null);

    Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default)
        => Task.FromResult(scope);

    Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default)
        => Task.FromResult(false);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    async Task<InspectionRecordPage> QueryPageAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default)
    {
        var data = await QueryAsync(query, ct).ConfigureAwait(false);
        return new InspectionRecordPage
        {
            Data = data,
            Total = data.Count,
            Offset = query.Offset,
            Limit = query.Limit
        };
    }

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

