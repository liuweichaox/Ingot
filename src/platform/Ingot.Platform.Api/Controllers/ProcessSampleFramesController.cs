using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-executions/{executionId}/samples")]
public sealed class ProcessSampleFramesController(
    ITimeSeriesStore timeSeries,
    PlatformUserResolver userResolver) : ControllerBase
{
    private const int MaximumPageSize = 10_000;

    [HttpGet]
    public async Task<IActionResult> Query(
        string executionId,
        [FromQuery] DateTimeOffset? afterOccurredAt,
        [FromQuery] long? afterFrameId,
        [FromQuery] int limit = MaximumPageSize,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return BadRequest(new { error = "运行编号格式不正确。" });
        if (limit is < 1 or > MaximumPageSize)
            return BadRequest(new { error = $"Limit 必须在 1 到 {MaximumPageSize} 之间。" });
        if (afterOccurredAt.HasValue != afterFrameId.HasValue)
            return BadRequest(new { error = "采集帧游标必须同时包含时间和帧编号。" });

        var frames = await timeSeries.QueryFramesAsync(new TimeSeriesQuery
        {
            ExecutionId = executionId.Trim(),
            AfterOccurredAt = afterOccurredAt,
            AfterFrameId = afterFrameId,
            Limit = limit + 1
        }, ct).ConfigureAwait(false);
        var page = frames.Take(limit).Select(static frame => new ProcessSampleFrameItem
        {
            FrameId = frame.IngestId,
            OccurredAt = frame.OccurredAt,
            RecordedAt = frame.RecordedAt,
            IngestedAt = frame.IngestedAt,
            PhaseCode = frame.PhaseCode,
            Values = frame.NumericValues
        }).ToArray();
        ProcessSampleFrameCursor? nextCursor = null;
        if (frames.Count > limit)
        {
            var last = page[^1];
            nextCursor = new ProcessSampleFrameCursor
            {
                OccurredAt = last.OccurredAt,
                FrameId = last.FrameId
            };
        }

        return Ok(new ProcessSampleFramePage
        {
            Data = page,
            NextCursor = nextCursor
        });
    }
}

public sealed record ProcessSampleFramePage
{
    public required IReadOnlyList<ProcessSampleFrameItem> Data { get; init; }
    public ProcessSampleFrameCursor? NextCursor { get; init; }
}

public sealed record ProcessSampleFrameItem
{
    public long FrameId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? IngestedAt { get; init; }
    public string? PhaseCode { get; init; }
    public required IReadOnlyDictionary<string, double> Values { get; init; }
}

public sealed record ProcessSampleFrameCursor
{
    public required DateTimeOffset OccurredAt { get; init; }
    public long FrameId { get; init; }
}
