// 以游标和总行数预算读取时序帧，超限时在返回部分结果前失败。
namespace Ingot.Platform.Application.TimeSeries;

public static class TimeSeriesFrameReader
{
    private const int PageSize = 10_000;

    public static async Task<IReadOnlyList<ProcessSampleFrame>> QueryAllAsync(
        ITimeSeriesStore store,
        TimeSeriesQuery query,
        CancellationToken ct = default,
        int maximumFrames = 100_000)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(query);
        if (maximumFrames is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maximumFrames), "时序帧预算必须在 1 到 1000000 之间。");
        var result = new List<ProcessSampleFrame>();
        DateTimeOffset? afterOccurredAt = null;
        long? afterFrameId = null;
        while (true)
        {
            var remaining = maximumFrames - result.Count;
            var requestedLimit = Math.Min(PageSize, remaining + 1);
            var page = await store.QueryFramesAsync(query with
            {
                AfterOccurredAt = afterOccurredAt,
                AfterFrameId = afterFrameId,
                Limit = requestedLimit
            }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            if (page.Count > remaining)
                throw new TimeSeriesQueryLimitExceededException(maximumFrames);
            result.AddRange(page);
            var last = page[^1];
            if (afterOccurredAt == last.OccurredAt && afterFrameId == last.IngestId)
                throw new InvalidOperationException("时序帧查询游标没有前进。");
            afterOccurredAt = last.OccurredAt;
            afterFrameId = last.IngestId;
            if (page.Count < requestedLimit)
                break;
        }
        return result;
    }
}

/// <summary>表示时序查询超过应用允许的硬帧数预算。</summary>
public sealed class TimeSeriesQueryLimitExceededException(int maximumFrames) : InvalidOperationException(
    $"时序查询超过 {maximumFrames} 帧预算；请缩小站点、过程执行或时间范围。")
{
    public int MaximumFrames { get; } = maximumFrames;
}
