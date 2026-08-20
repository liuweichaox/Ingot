// 实现基础设施适配器 InspectionProductionEventReader，满足应用层端口而不改变领域契约。

using Ingot.Contracts.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionProductionEventReader(IPlatformEventStore events)
    : IInspectionProductionEventReader
{
    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryCompletedAsync(
        string? executionId,
        CancellationToken ct = default)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(new PlatformEventQuery
            {
                ExecutionId = executionId,
                EventType = "process.execution.completed",
                AfterIngestId = cursor,
                Limit = 500
            }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
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
