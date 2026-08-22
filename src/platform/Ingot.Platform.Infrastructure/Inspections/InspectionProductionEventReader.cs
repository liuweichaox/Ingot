// 分页读取待检工作流所需的生产完成事件并执行扫描保护。

using Ingot.Contracts.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionProductionEventReader(IPlatformEventStore events)
    : IInspectionProductionEventReader
{
    private const int MaximumCompletedEvents = 20_000;

    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryCompletedAsync(
        string? executionId,
        string? siteId,
        CancellationToken ct = default)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(new PlatformEventQuery
            {
                ExecutionId = executionId,
                SiteId = siteId,
                EventType = "process.execution.completed",
                AfterIngestId = cursor,
                Limit = 500
            }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            if (result.Count + page.Count > MaximumCompletedEvents)
            {
                throw new InspectionQueryLimitExceededException(
                    $"待检任务超过 {MaximumCompletedEvents} 条扫描上限，请缩小站点范围或归档历史任务。");
            }
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("待检任务查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
