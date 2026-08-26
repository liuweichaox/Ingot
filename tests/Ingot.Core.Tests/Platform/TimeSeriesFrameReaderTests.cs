// 验证平台组件 TimeSeriesFrameReader 的成功、拒绝和安全边界。

using Ingot.Platform.Infrastructure.TimeSeries;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class TimeSeriesFrameReaderTests
{
    [Fact]
    public async Task QueryAllAsync_UsesStableCompositeCursorBeyondOnePage()
    {
        var at = DateTimeOffset.Parse("2026-08-17T00:00:00Z");
        var source = Enumerable.Range(1, 10_005)
            .Select(index => new ProcessSampleFrame
            {
                EventId = $"event-{index}",
                IngestId = index,
                OccurredAt = at.AddSeconds(index / 3),
                RecordedAt = at.AddSeconds(index / 3),
                NumericValues = new Dictionary<string, double> { ["signal"] = index }
            }).ToArray();
        var store = new FrameStore(source);

        var result = await TimeSeriesFrameReader.QueryAllAsync(store, new TimeSeriesQuery());

        Assert.Equal(10_005, result.Count);
        Assert.Equal(10_005, result.Select(static frame => frame.EventId).Distinct().Count());
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task QueryAllAsync_FailsBeforeReturningFramesBeyondBudget()
    {
        var at = DateTimeOffset.Parse("2026-08-17T00:00:00Z");
        var source = Enumerable.Range(1, 10_005)
            .Select(index => new ProcessSampleFrame
            {
                EventId = $"event-{index}",
                IngestId = index,
                OccurredAt = at.AddSeconds(index),
                RecordedAt = at.AddSeconds(index)
            }).ToArray();
        var store = new FrameStore(source);

        var error = await Assert.ThrowsAsync<TimeSeriesQueryLimitExceededException>(() =>
            TimeSeriesFrameReader.QueryAllAsync(
                store,
                new TimeSeriesQuery(),
                maximumFrames: 10_000));

        Assert.Equal(10_000, error.MaximumFrames);
        Assert.Equal(2, store.QueryCount);
    }

    private sealed class FrameStore(IReadOnlyList<ProcessSampleFrame> rows) : ITimeSeriesStore
    {
        public int QueryCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SignalSample>> QueryAsync(
            TimeSeriesQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SignalSample>>([]);

        public Task<IReadOnlyList<ProcessSampleFrame>> QueryFramesAsync(
            TimeSeriesQuery query,
            CancellationToken ct = default)
        {
            QueryCount++;
            var filtered = rows.Where(frame =>
                !query.AfterOccurredAt.HasValue ||
                frame.OccurredAt > query.AfterOccurredAt.Value ||
                frame.OccurredAt == query.AfterOccurredAt.Value && frame.IngestId > query.AfterFrameId!.Value);
            return Task.FromResult<IReadOnlyList<ProcessSampleFrame>>(
                filtered.OrderBy(static frame => frame.OccurredAt)
                    .ThenBy(static frame => frame.IngestId)
                    .Take(query.Limit)
                    .ToArray());
        }
    }
}
