using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface ICycleComparisonService
{
    Task<CycleComparisonResult?> CompareWithHistoryAsync(
        string correlationId,
        int limit,
        CancellationToken ct = default);

    Task<CycleComparisonResult?> CompareSelectedAsync(
        string baselineCycleId,
        IReadOnlyList<string> cycleIds,
        CancellationToken ct = default);
}
