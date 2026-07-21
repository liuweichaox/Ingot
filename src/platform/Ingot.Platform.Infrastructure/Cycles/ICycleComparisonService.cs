using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface ICycleComparisonService
{
    Task<CycleComparisonResult?> CompareWithHistoryAsync(
        string correlationId,
        int limit,
        CancellationToken ct = default);
}
