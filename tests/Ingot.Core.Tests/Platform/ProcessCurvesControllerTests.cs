using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Application.TimeSeries;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessCurvesControllerTests
{
    [Fact]
    public async Task Query_returns_selected_series_without_exposing_event_json()
    {
        var occurredAt = DateTimeOffset.Parse("2026-08-17T10:00:00Z");
        var store = new RecordingTimeSeriesStore(
        [
            Frame(101, occurredAt, 601, 11),
            Frame(102, occurredAt.AddSeconds(1), 602, 12),
            Frame(103, occurredAt.AddSeconds(2), 603, 13)
        ]);
        var controller = Controller(store);

        var result = await controller.Query(
            " execution-01 ",
            "temperature,pressure",
            null,
            null,
            100);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ProcessCurveResponse>(ok.Value);
        Assert.Equal(3, response.TotalFrameCount);
        Assert.Equal(6, response.ReturnedPointCount);
        Assert.False(response.Downsampled);
        Assert.Equal(["temperature", "pressure"], response.Series.Select(static item => item.SignalCode));
        Assert.Equal(601, response.Series[0].Points[0].Value);
        Assert.Equal("execution-01", store.Query!.ExecutionId);
    }

    [Fact]
    public async Task Query_downsamples_each_signal_while_preserving_extrema()
    {
        var occurredAt = DateTimeOffset.Parse("2026-08-17T10:00:00Z");
        var frames = Enumerable.Range(0, 1_000)
            .Select(index => Frame(
                index + 1,
                occurredAt.AddSeconds(index),
                index == 501 ? 50_000 : index,
                index))
            .ToArray();
        var controller = Controller(new RecordingTimeSeriesStore(frames));

        var result = await controller.Query("execution-01", "temperature", null, null, 100);

        var response = Assert.IsType<ProcessCurveResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var series = Assert.Single(response.Series);
        Assert.True(response.Downsampled);
        Assert.True(series.Points.Count <= 100);
        Assert.Contains(series.Points, static point => point.Value == 50_000);
        Assert.Equal(0, series.Points[0].Value);
        Assert.Equal(999, series.Points[^1].Value);
    }

    [Fact]
    public async Task Query_requires_at_least_one_signal()
    {
        var controller = Controller(new RecordingTimeSeriesStore([]));

        var result = await controller.Query("execution-01", null, null, null);

        var invalid = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.Equal("request.invalid", Assert.IsType<ApiProblemDetails>(invalid.Value).Code);
    }

    private static ProcessCurvesController Controller(ITimeSeriesStore store)
        => new(new ProcessCurveQueryService(store), new PlatformUserResolver(new DevelopmentEnvironment()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ProcessSampleFrame Frame(
        long frameId,
        DateTimeOffset occurredAt,
        double temperature,
        double pressure)
        => new()
        {
            EventId = $"event-{frameId}",
            IngestId = frameId,
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            PhaseCode = "heating",
            NumericValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["temperature"] = temperature,
                ["pressure"] = pressure
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
            var result = frames
                .Where(frame => query.AfterOccurredAt is null ||
                                frame.OccurredAt > query.AfterOccurredAt.Value ||
                                frame.OccurredAt == query.AfterOccurredAt.Value &&
                                frame.IngestId > query.AfterFrameId!.Value)
                .Take(query.Limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ProcessSampleFrame>>(result);
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
