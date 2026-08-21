using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Application.Inspections;

public sealed class InspectionExecutionReferenceValidator
{
    private readonly IExecutionBoundaryStore _boundaryStore;

    public InspectionExecutionReferenceValidator(IExecutionBoundaryStore boundaryStore)
    {
        _boundaryStore = boundaryStore ?? throw new ArgumentNullException(nameof(boundaryStore));
    }

    public async Task<(bool IsValid, string? ErrorReason)> ValidateExecutionReferenceAsync(
        string siteId,
        string executionId,
        DateTimeOffset inspectionTime,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return (false, "检验记录的 ExecutionId 不能为空。");

        var boundary = await _boundaryStore.GetBoundaryAsync(siteId, executionId, ct).ConfigureAwait(false);
        if (boundary is null)
            return (false, $"运行 {executionId} 不存在或未被识别。");

        var tolerance = TimeSpan.FromMinutes(5);
        var effectiveStartTime = boundary.StartedAt - tolerance;
        var effectiveEndTime = (boundary.EndedAt ?? DateTimeOffset.UtcNow) + tolerance;

        if (inspectionTime < effectiveStartTime)
            return (false,
                $"检验时间 {inspectionTime:O} 早于运行开始时间 {boundary.StartedAt:O}（容限 {tolerance.TotalMinutes} 分钟）。");

        if (boundary.EndedAt.HasValue && inspectionTime > effectiveEndTime)
            return (false,
                $"检验时间 {inspectionTime:O} 晚于运行结束时间 {boundary.EndedAt:O}（容限 {tolerance.TotalMinutes} 分钟）。");

        return (true, null);
    }

    public async Task<(bool IsConsistent, string? ErrorReason)> ValidateBatchConsistencyAsync(
        IReadOnlyList<InspectionRecordInput> records,
        string siteId,
        CancellationToken ct)
    {
        if (records.Count == 0)
            return (true, null);

        var executionIds = records
            .Select(r => r.ExecutionId)
            .Distinct()
            .ToList();

        if (executionIds.Count > 1)
            return (false, $"批次中的检验记录来自多个运行：{string.Join(", ", executionIds.Take(3))}。" +
                "同一批检验操作应关联到同一个运行。");

        if (executionIds.Count == 1 && string.IsNullOrEmpty(executionIds[0]))
            return (false, "批次中的所有检验记录都缺少 ExecutionId。");

        var times = records
            .Select(r => r.InspectionTime)
            .OrderBy(t => t)
            .ToList();

        var maxTimeGap = TimeSpan.Zero;
        for (var i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            maxTimeGap = gap > maxTimeGap ? gap : maxTimeGap;
        }

        if (maxTimeGap > TimeSpan.FromHours(1))
            return (false, $"批次中检验时间跨度过大（最大间隔 {maxTimeGap.TotalMinutes} 分钟）。" +
                "同一批检验应在较短时间内完成。");

        return (true, null);
    }
}

public sealed record InspectionRecordInput
{
    public required string ExecutionId { get; init; }
    public required DateTimeOffset InspectionTime { get; init; }
    public required Dictionary<string, object?> Data { get; init; }
}
