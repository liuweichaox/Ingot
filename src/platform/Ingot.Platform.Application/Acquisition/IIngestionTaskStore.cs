using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>持久化采集任务定义、版本和运行状态。</summary>
public interface IIngestionTaskStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default);
    Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default);

    /// <summary>
    ///     在单个数据库事务内发布指定版本并将同任务的其他 published 版本置为 retired。
    ///     解决"读-改-写循环发布"在并发下可能残留两个 published 版本、
    ///     被 /active 同时下发到边缘节点的竞态。
    /// </summary>
    Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default);

    Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default);
}
