namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>向交付层提供过程执行边界的只读查询用例。</summary>
public sealed class ExecutionBoundaryQueries(IExecutionBoundaryStore boundaries)
{
    public Task<ExecutionBoundary?> GetAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct = default)
        => boundaries.GetBoundaryAsync(siteId, sourceExecutionId, ct);
}
