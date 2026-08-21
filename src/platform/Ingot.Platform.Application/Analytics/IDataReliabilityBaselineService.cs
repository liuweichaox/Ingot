using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Application.Analytics;

public interface IDataReliabilityBaselineService
{
    Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default);
}
