using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Events;

public interface IPlatformEventStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        PlatformEventQuery query,
        CancellationToken ct = default);

    async Task<IReadOnlyList<PlatformProductionEvent>> QueryByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        var result = new List<PlatformProductionEvent>();
        foreach (var executionId in executionIds
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal))
        {
            var cursor = 0L;
            while (true)
            {
                var page = await QueryAsync(new PlatformEventQuery
                {
                    ExecutionId = executionId,
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

    async Task<IReadOnlyList<PlatformProcessExecutionSummarySource>> QueryExecutionSummarySourcesAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        var rows = await QueryByExecutionIdsAsync(executionIds, ct).ConfigureAwait(false);
        return rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId))
            .GroupBy(static row => row.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(static group => new PlatformProcessExecutionSummarySource
            {
                ExecutionId = group.Key,
                SampleCount = 0,
                Events = group.ToArray()
            })
            .ToArray();
    }

    Task<DataObjectPage> QueryDataObjectsAsync(
        DataObjectQuery query,
        CancellationToken ct = default)
        => Task.FromResult(new DataObjectPage
        {
            Limit = query.Limit,
            Offset = query.Offset
        });

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
