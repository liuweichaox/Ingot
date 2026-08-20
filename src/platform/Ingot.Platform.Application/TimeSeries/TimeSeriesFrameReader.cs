// 实现应用层用例 TimeSeriesFrameReader，集中承载可独立测试的业务规则。

namespace Ingot.Platform.Application.TimeSeries;

public static class TimeSeriesFrameReader
{
    private const int PageSize = 10_000;

    public static async Task<IReadOnlyList<ProcessSampleFrame>> QueryAllAsync(
        ITimeSeriesStore store,
        TimeSeriesQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(query);
        var result = new List<ProcessSampleFrame>();
        DateTimeOffset? afterOccurredAt = null;
        long? afterFrameId = null;
        while (true)
        {
            var page = await store.QueryFramesAsync(query with
            {
                AfterOccurredAt = afterOccurredAt,
                AfterFrameId = afterFrameId,
                Limit = PageSize
            }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            result.AddRange(page);
            var last = page[^1];
            if (afterOccurredAt == last.OccurredAt && afterFrameId == last.IngestId)
                throw new InvalidOperationException("时序帧查询游标没有前进。");
            afterOccurredAt = last.OccurredAt;
            afterFrameId = last.IngestId;
            if (page.Count < PageSize)
                break;
        }
        return result;
    }
}
