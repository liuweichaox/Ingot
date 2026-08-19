using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Application.Inspections;

/// <summary>
/// 检验记录与运行的关联验证器。
/// 确保检验记录的 ExecutionId 有效，时间戳在对应运行的范围内。
/// 这个验证器专注于执行边界相关的检查（与 InspectionRecordValidator 分工不同）。
/// </summary>
public sealed class InspectionExecutionReferenceValidator
{
    private readonly IExecutionBoundaryStore _boundaryStore;

    public InspectionExecutionReferenceValidator(IExecutionBoundaryStore boundaryStore)
    {
        _boundaryStore = boundaryStore ?? throw new ArgumentNullException(nameof(boundaryStore));
    }

    /// <summary>
    /// 验证检验记录是否与有效的运行关联。
    /// </summary>
    /// <param name="siteId">生产单元。</param>
    /// <param name="executionId">运行标识。</param>
    /// <param name="inspectionTime">检验执行的时间。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// - (true, null) 如果运行存在且检验时间在运行范围内。
    /// - (false, reason) 如果运行不存在或检验时间超出范围。
    /// </returns>
    public async Task<(bool IsValid, string? ErrorReason)> ValidateExecutionReferenceAsync(
        string siteId,
        string executionId,
        DateTimeOffset inspectionTime,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return (false, "检验记录的 ExecutionId 不能为空。");

        // 查询运行边界是否存在
        var boundary = await _boundaryStore.GetBoundaryAsync(siteId, executionId, ct).ConfigureAwait(false);
        if (boundary is null)
            return (false, $"运行 {executionId} 不存在或未被识别。");

        // 检查时间戳是否在运行范围内（容限 ±5 分钟以应对时钟偏差）
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

    /// <summary>
    /// 验证一批检验记录的一致性（它们是否来自同一实验批次/工作单元）。
    /// </summary>
    /// <param name="records">待验证的检验记录。</param>
    /// <param name="siteId">生产单元。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// - (true, null) 如果所有记录属于同一运行。
    /// - (false, reason) 如果记录来自不同运行或某个运行不存在。
    /// </returns>
    public async Task<(bool IsConsistent, string? ErrorReason)> ValidateBatchConsistencyAsync(
        IReadOnlyList<InspectionRecordInput> records,
        string siteId,
        CancellationToken ct)
    {
        if (records.Count == 0)
            return (true, null);

        // 所有记录应来自同一个 ExecutionId
        var executionIds = records
            .Select(r => r.ExecutionId)
            .Distinct()
            .ToList();

        if (executionIds.Count > 1)
            return (false, $"批次中的检验记录来自多个运行：{string.Join(", ", executionIds.Take(3))}。" +
                "同一批检验操作应关联到同一个运行。");

        if (executionIds.Count == 1 && string.IsNullOrEmpty(executionIds[0]))
            return (false, "批次中的所有检验记录都缺少 ExecutionId。");

        // 检查时间戳的离散度（来自同一运行的检验不应间隔超过 1 小时）
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

/// <summary>
/// 检验记录的输入模型（用于验证阶段）。
/// </summary>
public sealed record InspectionRecordInput
{
    public required string ExecutionId { get; init; }
    public required DateTimeOffset InspectionTime { get; init; }
    public required Dictionary<string, object?> Data { get; init; }
}
