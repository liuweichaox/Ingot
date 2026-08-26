// 读取一次生产运行的多信号时序，并在总点数预算内为每条曲线保形降采样。
namespace Ingot.Platform.Application.TimeSeries;

/// <summary>
/// Queries every requested process signal and shares a bounded response-point budget across the returned series.
/// </summary>
public sealed class ProcessCurveQueryService(ITimeSeriesStore timeSeries)
{
    private const int MaximumReturnedPoints = 50_000;

    public async Task<ProcessCurveResponse> QueryAsync(
        string siteId,
        string executionId,
        IReadOnlyList<string> signalCodes,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int maximumPoints,
        CancellationToken ct = default)
    {
        var frames = await TimeSeriesFrameReader.QueryAllAsync(
            timeSeries,
            new TimeSeriesQuery
            {
                SiteId = siteId,
                ExecutionId = executionId,
                From = from,
                To = to
            },
            ct).ConfigureAwait(false);
        var maximumPointsPerSeries = Math.Min(
            maximumPoints,
            Math.Max(4, MaximumReturnedPoints / Math.Max(1, signalCodes.Count)));
        var series = signalCodes.Select(code =>
        {
            var source = frames
                .Where(frame => frame.NumericValues.ContainsKey(code))
                .Select(frame => new ProcessCurvePoint
                {
                    FrameId = frame.IngestId,
                    OccurredAt = frame.OccurredAt,
                    PhaseCode = frame.PhaseCode,
                    Value = frame.NumericValues[code]
                })
                .ToArray();
            return new ProcessCurveSeries
            {
                SignalCode = code,
                SourcePointCount = source.Length,
                Points = DownsampleMinMax(source, maximumPointsPerSeries)
            };
        }).ToArray();
        return new ProcessCurveResponse
        {
            TotalFrameCount = frames.Count,
            ReturnedPointCount = series.Sum(static item => item.Points.Count),
            Downsampled = series.Any(static item => item.SourcePointCount > item.Points.Count),
            Series = series
        };
    }

    internal static IReadOnlyList<ProcessCurvePoint> DownsampleMinMax(
        IReadOnlyList<ProcessCurvePoint> source,
        int maximumPoints)
    {
        if (source.Count <= maximumPoints)
            return source;

        var result = new List<ProcessCurvePoint>(maximumPoints) { source[0] };
        var bucketCount = Math.Max(1, (maximumPoints - 2) / 2);
        var interiorCount = source.Count - 2;
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = 1 + (int)((long)bucket * interiorCount / bucketCount);
            var end = 1 + (int)((long)(bucket + 1) * interiorCount / bucketCount);
            if (end <= start)
                continue;
            var minimum = source[start];
            var maximum = source[start];
            for (var index = start + 1; index < end; index++)
            {
                if (source[index].Value < minimum.Value)
                    minimum = source[index];
                if (source[index].Value > maximum.Value)
                    maximum = source[index];
            }
            if (minimum.FrameId == maximum.FrameId)
            {
                result.Add(minimum);
            }
            else if (minimum.OccurredAt < maximum.OccurredAt ||
                     minimum.OccurredAt == maximum.OccurredAt && minimum.FrameId < maximum.FrameId)
            {
                result.Add(minimum);
                result.Add(maximum);
            }
            else
            {
                result.Add(maximum);
                result.Add(minimum);
            }
        }
        result.Add(source[^1]);
        return result;
    }
}

public sealed record ProcessCurveResponse
{
    public int TotalFrameCount { get; init; }
    public int ReturnedPointCount { get; init; }
    public bool Downsampled { get; init; }
    public required IReadOnlyList<ProcessCurveSeries> Series { get; init; }
}

public sealed record ProcessCurveSeries
{
    public required string SignalCode { get; init; }
    public int SourcePointCount { get; init; }
    public required IReadOnlyList<ProcessCurvePoint> Points { get; init; }
}

public sealed record ProcessCurvePoint
{
    public long FrameId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? PhaseCode { get; init; }
    public double Value { get; init; }
}
