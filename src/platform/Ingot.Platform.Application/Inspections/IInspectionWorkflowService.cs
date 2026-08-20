using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>执行检验记录创建、复核和处置的应用工作流。</summary>
public interface IInspectionWorkflowService
{
    Task<InspectionTask?> GetTaskAsync(string executionId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionTask>> QueryTasksAsync(
        string? status,
        int limit,
        CancellationToken ct = default);

    Task<InspectionTaskPage> QueryTaskPageAsync(
        string? status,
        int offset,
        int limit,
        CancellationToken ct = default);

    Task<InspectionTaskSummary> GetSummaryAsync(CancellationToken ct = default);
}
