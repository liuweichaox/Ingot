using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Infrastructure.Acquisition;

public interface IIngestionConfigurationStore
{
    Task<IReadOnlyList<IngestionTaskTemplate>> ListTemplatesAsync(CancellationToken ct = default);
    Task<IngestionTaskTemplate?> GetTemplateAsync(string templateId, int version, CancellationToken ct = default);
    Task<IngestionTaskTemplate> UpsertTemplateAsync(IngestionTaskTemplate value, CancellationToken ct = default);
    Task<IngestionTaskTemplate> PublishTemplateExclusiveAsync(IngestionTaskTemplate value, CancellationToken ct = default);
    Task<bool> DeleteTemplateAsync(string templateId, int version, CancellationToken ct = default);

    Task<IReadOnlyList<DataSourceInstance>> ListDataSourcesAsync(CancellationToken ct = default);
    Task<DataSourceInstance?> GetDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default);
    Task<DataSourceInstance> UpsertDataSourceAsync(DataSourceInstance value, CancellationToken ct = default);
    Task<DataSourceInstance> PublishDataSourceExclusiveAsync(DataSourceInstance value, CancellationToken ct = default);
    Task<IReadOnlyList<DataSourceInstance>> SaveDataSourcesAsync(
        IReadOnlyList<DataSourceInstance> values,
        CancellationToken ct = default);
    Task<bool> DeleteDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default);

    Task<IReadOnlyList<IngestionTaskBinding>> ListBindingsAsync(CancellationToken ct = default);
    Task<IngestionTaskBinding?> GetBindingAsync(string taskId, int version, CancellationToken ct = default);

    /// <summary>在一个事务中保存任务绑定及其不可变运行快照，供单个或批量实例化使用。</summary>
    Task<IReadOnlyList<IngestionTask>> SaveMaterializedTasksAsync(
        IReadOnlyList<(IngestionTaskBinding Binding, IngestionTask Task)> values,
        CancellationToken ct = default);

    /// <summary>原子保存从首台已验证任务提取出的模板、数据源、绑定和带来源引用的运行快照。</summary>
    Task<ReusableIngestionConfiguration> SaveExtractedAsync(
        ReusableIngestionConfiguration value,
        CancellationToken ct = default);
}
