using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionWorkflowService
{
    Task<InspectionTask?> GetTaskAsync(string operationRunId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionTask>> QueryTasksAsync(
        string? status,
        int limit,
        CancellationToken ct = default);
}
