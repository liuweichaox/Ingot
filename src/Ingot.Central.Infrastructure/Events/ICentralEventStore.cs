using Ingot.Contracts.Events;

namespace Ingot.Central.Infrastructure.Events;

public interface ICentralEventStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
        CentralEventQuery query,
        CancellationToken ct = default);

    /// <summary>
    ///     对同一过滤范围做聚合统计（总条数、最早/最新 OccurredAt），不受查询 Limit 截断。
    ///     用于数据质量的准确"新鲜度"与总量，避免"拉 N 行取 max"的近似。
    /// </summary>
    Task<CentralEventScopeStats> GetScopeStatsAsync(
        CentralEventQuery query,
        CancellationToken ct = default);

    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

/// <summary>某个查询范围的聚合统计。</summary>
public sealed record CentralEventScopeStats
{
    public long Count { get; init; }

    public DateTimeOffset? LatestOccurredAt { get; init; }

    public DateTimeOffset? EarliestOccurredAt { get; init; }
}
