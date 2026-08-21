using System.Threading;
using System.Threading.Tasks;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;

namespace Ingot.Edge.Application.Abstractions;

public interface IPlatformReportingClient
{

    bool TryInitialize(string? listenUrls);

    Task RegisterWithRetryAsync(CancellationToken ct = default);

    Task SendHeartbeatAsync(
        EdgeAcquisitionRuntimeStatus? acquisitionStatus,
        EdgeDeliveryRuntimeStatus? deliveryStatus,
        CancellationToken ct = default);

    int HeartbeatIntervalSeconds { get; }
}
