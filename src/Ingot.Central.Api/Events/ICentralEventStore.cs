using Ingot.Contracts.Events;

namespace Ingot.Central.Api.Events;

public interface ICentralEventStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
        CentralEventQuery query,
        CancellationToken ct = default);

    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
