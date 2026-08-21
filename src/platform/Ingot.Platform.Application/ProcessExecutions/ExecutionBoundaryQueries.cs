namespace Ingot.Platform.Application.ProcessExecutions;

public sealed class ExecutionBoundaryQueries(IExecutionBoundaryStore boundaries)
{
    public Task<ExecutionBoundary?> GetAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct = default)
        => boundaries.GetBoundaryAsync(siteId, sourceExecutionId, ct);
}
