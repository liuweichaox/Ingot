using Ingot.Contracts.Events;
using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Infrastructure.Events;

public interface IPlatformEventStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        PlatformEventQuery query,
        CancellationToken ct = default);

    async Task<IReadOnlyList<PlatformProductionEvent>> QueryByCorrelationIdsAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken ct = default)
    {
        var result = new List<PlatformProductionEvent>();
        foreach (var correlationId in correlationIds
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal))
        {
            var cursor = 0L;
            while (true)
            {
                var page = await QueryAsync(new PlatformEventQuery
                {
                    CorrelationId = correlationId,
                    AfterIngestId = cursor,
                    Limit = 500
                }, ct).ConfigureAwait(false);
                if (page.Count == 0)
                    break;
                result.AddRange(page);
                var next = page.Max(static row => row.IngestId);
                if (next <= cursor || page.Count < 500)
                    break;
                cursor = next;
            }
        }
        return result;
    }

    Task<DataObjectPage> QueryDataObjectsAsync(
        DataObjectQuery query,
        CancellationToken ct = default)
        => Task.FromResult(new DataObjectPage
        {
            Limit = query.Limit,
            Offset = query.Offset
        });

    /// <summary>
    ///     对同一过滤范围做聚合统计（总条数、最早/最新 OccurredAt），不受查询 Limit 截断。
    ///     用于数据质量的准确"新鲜度"与总量，避免"拉 N 行取 max"的近似。
    /// </summary>
    Task<PlatformEventScopeStats> GetScopeStatsAsync(
        PlatformEventQuery query,
        CancellationToken ct = default);

    Task<bool> CanConnectAsync(CancellationToken ct = default);
}

/// <summary>某个查询范围的聚合统计。</summary>
public sealed record PlatformEventScopeStats
{
    public long Count { get; init; }

    public DateTimeOffset? LatestOccurredAt { get; init; }

    public DateTimeOffset? EarliestOccurredAt { get; init; }
}
