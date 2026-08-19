using Ingot.Domain.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>
/// 运行边界识别器：从生产事件流识别独立的运行边界。
///
/// 设计策略：
/// - 事件驱动：遇到 process.execution.started 和 process.execution.ended 事件时确定边界。
/// - 启发式修正：
///   * 无 process.execution.started 时，用第一条事件时间 + 启发式算法推断运行开始。
///   * 无 process.execution.ended 时，用超时时间（可配置，如 10 小时）推断运行结束。
///   * 处理乱序：边界确定后仍可接收晚到的事件；晚到超过阈值的分入新运行。
/// - 关键约束：ExecutionId 相同的事件必须分入同一运行（除非乱序超出阈值）。
/// </summary>
public interface IExecutionBoundaryRecognizer
{
    /// <summary>
    /// 从事件流识别运行边界。
    /// </summary>
    /// <param name="siteId">生产单元标识。</param>
    /// <param name="edgeId">采集节点标识。</param>
    /// <param name="events">已排序的生产事件（按 Seq 升序）。</param>
    /// <param name="options">识别配置选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>识别出的运行边界列表（无重叠、按时间顺序）。</returns>
    Task<IReadOnlyList<ExecutionBoundary>> RecognizeBoundariesAsync(
        string siteId,
        string edgeId,
        IReadOnlyList<ProductionEvent> events,
        ExecutionBoundaryRecognitionOptions options,
        CancellationToken ct);

    /// <summary>
    /// 针对新增事件修正已有的运行边界。
    /// 用于处理乱序事件：如果事件晚到但属于已完成的运行，应刷新该运行的 EndedAt。
    /// 如果事件晚到超过阈值（如 1 小时），则分入新运行。
    /// </summary>
    /// <param name="existingBoundary">已识别的运行边界。</param>
    /// <param name="lateArrivalEvent">晚到的事件。</param>
    /// <param name="options">识别配置选项。</param>
    /// <returns>
    /// - (修正后的 existingBoundary, null) 若事件属于该运行。
    /// - (原 existingBoundary, 新建立的运行边界) 若事件应分入新运行。
    /// </returns>
    ExecutionBoundaryAdjustment AdjustForLateArrival(
        ExecutionBoundary existingBoundary,
        ProductionEvent lateArrivalEvent,
        ExecutionBoundaryRecognitionOptions options);

    /// <summary>
    /// 根据缺口标记对运行边界进行标记。当检测到事件序列中的缺口时调用。
    /// </summary>
    /// <param name="boundary">受影响的运行边界。</param>
    /// <param name="gapDescription">缺口的描述（如"Seq 从 100 到 120 缺失"）。</param>
    /// <returns>更新标记后的边界。</returns>
    ExecutionBoundary MarkGapDetected(ExecutionBoundary boundary, string gapDescription);
}

/// <summary>
/// 运行边界识别的配置选项。
/// </summary>
public sealed class ExecutionBoundaryRecognitionOptions
{
    /// <summary>
    /// 缺少 process.execution.ended 事件时，经过多久后推断运行已结束。
    /// 默认 10 小时。
    /// </summary>
    public TimeSpan ExecutionTimeoutWithoutEndEvent { get; set; } = TimeSpan.FromHours(10);

    /// <summary>
    /// 晚到事件的容限。若事件到达时间晚于运行 EndedAt + 此值，则分入新运行。
    /// 默认 1 小时（应对生产环境的时钟偏差、网络延迟等）。
    /// </summary>
    public TimeSpan LateArrivalThreshold { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 是否严格要求 process.execution.started 和 process.execution.ended 事件。
    /// - true：若缺少这两个事件之一，标记为 Fragmented 置信度。
    /// - false：允许从其他事件类型推断运行边界。
    /// 默认 false（更宽松，适合试点环境）。
    /// </summary>
    public bool RequireExplicitStartEnd { get; set; } = false;

    /// <summary>
    /// 事件乱序的最大容限（以 Seq 差为单位）。
    /// 若晚到事件的 Seq 与已处理最大 Seq 的差超过此值，视为属于新运行。
    /// 默认 500（允许晚到的事件是已处理事件之前的）。
    /// </summary>
    public long MaxSeqDisorderTolerance { get; set; } = 500;
}

/// <summary>
/// 晚到事件对运行边界的调整结果。
/// </summary>
public sealed record ExecutionBoundaryAdjustment
{
    /// <summary>调整后的原运行边界（可能是原值，可能被修正）。</summary>
    public required ExecutionBoundary AdjustedExisting { get; init; }

    /// <summary>
    /// 若晚到事件应分入新运行，此为新建立的运行边界；否则为 null。
    /// </summary>
    public ExecutionBoundary? NewBoundary { get; init; }
}
