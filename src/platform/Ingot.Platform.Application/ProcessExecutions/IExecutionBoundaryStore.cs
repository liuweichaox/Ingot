namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>
/// 运行边界存储接口。用于查询已识别的运行边界。
/// 实现应由 Infrastructure 层提供（如 PostgresExecutionBoundaryStore）。
/// </summary>
public interface IExecutionBoundaryStore
{
    /// <summary>
    /// 根据 SiteId 和 SourceExecutionId 查询运行边界。
    /// </summary>
    /// <param name="siteId">生产单元。</param>
    /// <param name="sourceExecutionId">生产事件中的 ExecutionId（源标识）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// 匹配的运行边界，如果不存在则返回 null。
    /// </returns>
    Task<ExecutionBoundary?> GetBoundaryAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct);

    /// <summary>
    /// 保存新识别的运行边界。
    /// </summary>
    /// <param name="boundary">待保存的运行边界。</param>
    /// <param name="ct">取消令牌。</param>
    Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct);

    /// <summary>
    /// 更新已有的运行边界（如修正晚到事件后的边界）。
    /// </summary>
    /// <param name="boundary">待更新的运行边界。</param>
    /// <param name="ct">取消令牌。</param>
    Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct);

    /// <summary>
    /// 查询时间范围内的运行边界。
    /// </summary>
    /// <param name="siteId">生产单元。</param>
    /// <param name="from">时间范围开始（包含）。</param>
    /// <param name="to">时间范围结束（包含）。</param>
    /// <param name="limit">返回的最大条数。</param>
    /// <param name="offset">分页偏移。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>匹配的运行边界列表。</returns>
    Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
        string siteId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}
