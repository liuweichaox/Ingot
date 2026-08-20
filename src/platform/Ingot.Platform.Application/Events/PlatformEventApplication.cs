using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Events;

/// <summary>向交付层提供经过授权和分页约束的平台事件用例。</summary>
public sealed class PlatformEventApplication(IPlatformEventStore events)
{
    public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
        => events.IngestAsync(request, ct);

    public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        PlatformEventQuery query,
        CancellationToken ct = default)
        => events.QueryAsync(query, ct);

    public Task<PlatformEventScopeStats> GetScopeStatsAsync(
        PlatformEventQuery query,
        CancellationToken ct = default)
        => events.GetScopeStatsAsync(query, ct);

    public Task<DataObjectPage> QueryDataObjectsAsync(DataObjectQuery query, CancellationToken ct = default)
        => events.QueryDataObjectsAsync(query, ct);
}
