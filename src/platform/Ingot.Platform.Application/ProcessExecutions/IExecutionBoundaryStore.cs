// 定义真实运行边界识别、更新和失败投影重放的存储端口。
namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>保存站点内的运行边界，并显式支持失败投影重放。</summary>
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
        CancellationToken ct = default);
}
