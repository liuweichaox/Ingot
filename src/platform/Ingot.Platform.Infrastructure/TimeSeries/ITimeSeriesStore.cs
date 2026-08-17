namespace Ingot.Platform.Infrastructure.TimeSeries;

/// <summary>
/// Storage-neutral access to canonical process measurements. Implementations must preserve
/// event-time ordering, typed values, quality codes, units, and immutable run context.
/// </summary>
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
