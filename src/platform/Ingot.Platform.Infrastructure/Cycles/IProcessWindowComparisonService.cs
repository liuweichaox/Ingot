using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface IProcessWindowComparisonService
{
    Task<ProcessWindowComparisonResult> CompareAsync(
        ProcessWindowComparisonRequest request,
        CancellationToken ct = default);
}
