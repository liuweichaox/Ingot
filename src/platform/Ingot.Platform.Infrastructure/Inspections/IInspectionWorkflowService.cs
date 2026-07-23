using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionWorkflowService
{
    Task<InspectionTask?> GetTaskAsync(string operationRunId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionTask>> QueryTasksAsync(
        string? status,
        int limit,
        CancellationToken ct = default);

    async Task<InspectionTaskPage> QueryTaskPageAsync(
        string? status,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var all = await QueryTasksAsync(status, int.MaxValue, ct).ConfigureAwait(false);
        return new InspectionTaskPage
        {
            Data = all.Skip(offset).Take(limit).ToArray(),
            Total = all.Count,
            Offset = offset,
            Limit = limit
        };
    }

    Task<InspectionTaskSummary> GetSummaryAsync(CancellationToken ct = default);
}
