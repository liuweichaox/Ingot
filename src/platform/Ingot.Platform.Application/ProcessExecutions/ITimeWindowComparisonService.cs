using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

public interface ITimeWindowComparisonService
{
    Task<TimeWindowComparisonResult> CompareAsync(
        TimeWindowComparisonRequest request,
        CancellationToken ct = default);
}
