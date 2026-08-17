using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessSampleFramesControllerTests
{
    [Fact]
    public async Task Query_returns_native_frames_with_a_composite_next_cursor()
    {
        var occurredAt = DateTimeOffset.Parse("2026-08-17T10:00:00Z");
        var store = new RecordingTimeSeriesStore(
        [
            Frame(101, occurredAt, 601),
            Frame(102, occurredAt.AddSeconds(1), 602),
            Frame(103, occurredAt.AddSeconds(2), 603)
        ]);
        var controller = Controller(store);

        var result = await controller.Query(" execution-01 ", null, null, 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<ProcessSampleFramePage>(ok.Value);
        Assert.Equal(2, page.Data.Count);
        Assert.Equal(101, page.Data[0].FrameId);
        Assert.Equal(601, page.Data[0].Values["sensor.01"]);
        Assert.Equal(occurredAt.AddSeconds(1), page.NextCursor!.OccurredAt);
        Assert.Equal(102, page.NextCursor.FrameId);
        Assert.Equal("execution-01", store.Query!.ExecutionId);
        Assert.Equal(3, store.Query.Limit);
    }

    [Fact]
    public async Task Query_rejects_a_partial_cursor()
    {
        var controller = Controller(new RecordingTimeSeriesStore([]));

        var result = await controller.Query(
            "execution-01",
            DateTimeOffset.Parse("2026-08-17T10:00:00Z"),
            null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static ProcessSampleFramesController Controller(ITimeSeriesStore store)
        => new(store, new PlatformUserResolver(new DevelopmentEnvironment()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ProcessSampleFrame Frame(long frameId, DateTimeOffset occurredAt, double value)
        => new()
        {
            EventId = $"event-{frameId}",
            IngestId = frameId,
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            PhaseCode = "heating",
            NumericValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["sensor.01"] = value
            }
        };

    private sealed class RecordingTimeSeriesStore(IReadOnlyList<ProcessSampleFrame> frames) : ITimeSeriesStore
    {
        public TimeSeriesQuery? Query { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SignalSample>> QueryAsync(
            TimeSeriesQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SignalSample>>([]);

        public Task<IReadOnlyList<ProcessSampleFrame>> QueryFramesAsync(
            TimeSeriesQuery query,
            CancellationToken ct = default)
        {
            Query = query;
            return Task.FromResult(frames);
        }
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Ingot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
