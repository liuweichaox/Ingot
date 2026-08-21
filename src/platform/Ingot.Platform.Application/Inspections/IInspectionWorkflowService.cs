using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

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
