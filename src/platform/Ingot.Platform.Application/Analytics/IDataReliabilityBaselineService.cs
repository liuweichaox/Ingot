using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Application.Analytics;

/// <summary>计算分析准入所需的数据可靠性、覆盖率和排除原因基线。</summary>
public interface IDataReliabilityBaselineService
{
    Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default);
}
