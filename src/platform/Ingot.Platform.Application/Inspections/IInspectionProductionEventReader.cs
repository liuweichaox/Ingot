// 定义待检任务所需的生产完成事件只读边界。
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Inspections;

/// <summary>按运行和站点读取已完成生产事件。</summary>
public interface IInspectionProductionEventReader
{
    Task<IReadOnlyList<PlatformProductionEvent>> QueryCompletedAsync(
        string? executionId,
        string? siteId,
        CancellationToken ct = default);
}
