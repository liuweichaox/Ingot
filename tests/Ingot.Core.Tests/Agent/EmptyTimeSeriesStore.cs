using Ingot.Platform.Infrastructure.TimeSeries;

namespace Ingot.Core.Tests.Agent;

internal sealed class EmptyTimeSeriesStore : ITimeSeriesStore
{
    public static EmptyTimeSeriesStore Instance { get; } = new();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<SignalSample>> QueryAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SignalSample>>([]);

    public Task<IReadOnlyList<ProcessSampleFrame>> QueryFramesAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProcessSampleFrame>>([]);
}
