using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

public interface IIngestionTaskStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default);
    Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default);

    Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default);

    Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default);
}
