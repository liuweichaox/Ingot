using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface ITimeWindowComparisonService
{
    Task<TimeWindowComparisonResult> CompareAsync(
        TimeWindowComparisonRequest request,
        CancellationToken ct = default);
}
