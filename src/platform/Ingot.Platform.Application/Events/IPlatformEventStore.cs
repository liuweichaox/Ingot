// 定义生产事件写入、查询和分析投影所需的应用存储端口。
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Events;

/// <summary>保存正式生产事件，并提供按站点隔离的事件与汇总查询。</summary>
public interface IPlatformEventStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        PlatformEventQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlatformProductionEvent>> QueryByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlatformProcessExecutionSummarySource>> QueryExecutionSummarySourcesAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default);

    Task<DataObjectPage> QueryDataObjectsAsync(
        DataObjectQuery query,
        CancellationToken ct = default);

    Task<PlatformEventScopeStats> GetScopeStatsAsync(
        PlatformEventQuery query,
        CancellationToken ct = default);

    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

public sealed record PlatformProcessExecutionSummarySource
{
    public required string ExecutionId { get; init; }

    public int SampleCount { get; init; }

    public IReadOnlyList<PlatformProductionEvent> Events { get; init; } = [];
}

public sealed record PlatformEventScopeStats
{
    public long Count { get; init; }

    public DateTimeOffset? LatestOccurredAt { get; init; }

    public DateTimeOffset? EarliestOccurredAt { get; init; }
}
