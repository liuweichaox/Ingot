using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Infrastructure.Acquisition;

public interface IAcquisitionProfileStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AcquisitionProfile>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AcquisitionProfile>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<AcquisitionProfile?> GetAsync(string profileId, int version, CancellationToken ct = default);
    Task<AcquisitionProfile> UpsertAsync(AcquisitionProfile value, CancellationToken ct = default);

    /// <summary>
    ///     在单个数据库事务内发布指定版本并将同 profile 的其他 published 版本置为 retired。
    ///     解决"读-改-写循环发布"在并发下可能残留两个 published 版本、
    ///     被 /active 同时下发到边缘节点的竞态。
    /// </summary>
    Task<AcquisitionProfile> PublishExclusiveAsync(AcquisitionProfile published, CancellationToken ct = default);

    Task<bool> DeleteAsync(string profileId, int version, CancellationToken ct = default);
}
