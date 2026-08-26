// 编排采集配置的验证、版本化和发布，不直接承担协议连接或持久化实现。
using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>编排采集配置验证、版本化和发布用例。</summary>
public sealed class AcquisitionApplication(
    IIngestionTaskStore tasks,
    IIngestionConfigurationStore configurations)
{

    public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default)
        => tasks.ListAsync(ct);
    public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
        => tasks.ListPublishedForEdgeAsync(edgeId, ct);
    public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
        => tasks.GetAsync(taskId, version, ct);
    public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
        => tasks.UpsertAsync(value, ct);
    public Task<IngestionTask> PublishExclusiveAsync(IngestionTask value, CancellationToken ct = default)
        => tasks.PublishExclusiveAsync(value, ct);
    public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
        => tasks.DeleteAsync(taskId, version, ct);

    public Task<IReadOnlyList<IngestionTask>> ListTasksAsync(CancellationToken ct = default)
        => tasks.ListAsync(ct);
    public Task<IReadOnlyList<IngestionTask>> ListPublishedTasksForEdgeAsync(string edgeId, CancellationToken ct = default)
        => tasks.ListPublishedForEdgeAsync(edgeId, ct);
    public Task<IngestionTask?> GetTaskAsync(string taskId, int version, CancellationToken ct = default)
        => tasks.GetAsync(taskId, version, ct);
    public Task<IngestionTask> UpsertTaskAsync(IngestionTask value, CancellationToken ct = default)
        => tasks.UpsertAsync(value, ct);
    public Task<IngestionTask> PublishTaskExclusiveAsync(IngestionTask value, CancellationToken ct = default)
        => tasks.PublishExclusiveAsync(value, ct);
    public Task<bool> DeleteTaskAsync(string taskId, int version, CancellationToken ct = default)
        => tasks.DeleteAsync(taskId, version, ct);

    public Task<IReadOnlyList<IngestionTaskTemplate>> ListTemplatesAsync(CancellationToken ct = default)
        => configurations.ListTemplatesAsync(ct);
    public Task<IngestionTaskTemplate?> GetTemplateAsync(string templateId, int version, CancellationToken ct = default)
        => configurations.GetTemplateAsync(templateId, version, ct);
    public Task<IReadOnlyList<DataSourceInstance>> ListDataSourcesAsync(CancellationToken ct = default)
        => configurations.ListDataSourcesAsync(ct);
    public Task<DataSourceInstance?> GetDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default)
        => configurations.GetDataSourceAsync(dataSourceId, version, ct);
    public Task<IReadOnlyList<IngestionTaskBinding>> ListBindingsAsync(CancellationToken ct = default)
        => configurations.ListBindingsAsync(ct);
    public Task<IngestionTaskBinding?> GetBindingAsync(
        string taskId,
        int version,
        CancellationToken ct = default)
        => configurations.GetBindingAsync(taskId, version, ct);
}
