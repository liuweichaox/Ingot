using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-executions/{executionId}/curves")]
public sealed class ProcessCurvesController(
    ITimeSeriesStore timeSeries,
    PlatformUserResolver userResolver) : PlatformApiController
{
    private const int DefaultMaximumPoints = 2_000;
    private const int MaximumSignals = 32;

    [HttpGet]
    public async Task<IActionResult> Query(
        string executionId,
        [FromQuery] string? signalCodes,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int maxPoints = DefaultMaximumPoints,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return InvalidRequest("运行编号格式不正确。");
        if (from > to)
            return InvalidRequest("曲线开始时间不能晚于结束时间。");
        if (maxPoints is < 100 or > 10_000)
            return InvalidRequest("MaxPoints 必须在 100 到 10000 之间。");

        var requestedSignals = (signalCodes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedSignals.Length == 0)
            return InvalidRequest("至少选择一个过程信号。");
        if (requestedSignals.Length > MaximumSignals || requestedSignals.Any(static code => code.Length > 200))
            return InvalidRequest($"一次最多查询 {MaximumSignals} 个有效信号。");

        var frames = await TimeSeriesFrameReader.QueryAllAsync(
            timeSeries,
            new TimeSeriesQuery
            {
                ExecutionId = executionId.Trim(),
                From = from,
                To = to
            },
            ct).ConfigureAwait(false);
        var series = requestedSignals.Select(code =>
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
            var points = DownsampleMinMax(source, maxPoints);
            return new ProcessCurveSeries
            {
                SignalCode = code,
                SourcePointCount = source.Length,
                Points = points
            };
        }).ToArray();

        return Ok(new ProcessCurveResponse
        {
            TotalFrameCount = frames.Count,
            ReturnedPointCount = series.Sum(static item => item.Points.Count),
            Downsampled = series.Any(static item => item.SourcePointCount > item.Points.Count),
            Series = series
        });
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
