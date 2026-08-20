using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>在明确时间窗口和数据质量边界内比较过程与质量特征。</summary>
public interface ITimeWindowComparisonService
{
    Task<TimeWindowComparisonResult> CompareAsync(
        TimeWindowComparisonRequest request,
        CancellationToken ct = default);
}
