namespace Ingot.Platform.Application.ProcessExecutions;

public interface IExecutionBoundaryStore
{

    Task<ExecutionBoundary?> GetBoundaryAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct);

    Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct);

    Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct);

    Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
        string siteId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);

    Task<bool> ReplayFailedProjectionAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct = default) => Task.FromResult(false);
}
