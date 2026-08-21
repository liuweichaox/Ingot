namespace Ingot.Platform.Application.TimeSeries;

public interface ITimeSeriesStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SignalSample>> QueryAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProcessSampleFrame>> QueryFramesAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default);
}
