// 定义待检工作流查询以及扫描上限错误契约。
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>表示查询范围超过受控扫描上限。</summary>
public sealed class InspectionQueryLimitExceededException(string message) : Exception(message);

/// <summary>按站点生成待检任务、分页和状态汇总。</summary>
public interface IInspectionWorkflowService
{
    Task<InspectionTask?> GetTaskAsync(
        string executionId,
        CancellationToken ct = default,
        string? siteId = null);

    Task<IReadOnlyList<InspectionTask>> QueryTasksAsync(
        string? status,
        int limit,
        CancellationToken ct = default,
        string? siteId = null);

    Task<InspectionTaskPage> QueryTaskPageAsync(
        string? status,
        int offset,
        int limit,
        CancellationToken ct = default,
        string? siteId = null);

    Task<InspectionTaskSummary> GetSummaryAsync(
        CancellationToken ct = default,
        string? siteId = null);
}
