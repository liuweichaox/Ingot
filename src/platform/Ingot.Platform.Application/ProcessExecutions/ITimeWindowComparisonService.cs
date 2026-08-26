// 定义连续过程窗口比较的站点隔离应用边界。
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>比较同一授权站点内的连续过程时间窗口。</summary>
public interface ITimeWindowComparisonService
{
    Task<TimeWindowComparisonResult> CompareAsync(
        TimeWindowComparisonRequest request,
        string siteId,
        CancellationToken ct = default);
}
