using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

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

    Task<IReadOnlyList<IngestionTask>> SaveMaterializedTasksAsync(
        IReadOnlyList<(IngestionTaskBinding Binding, IngestionTask Task)> values,
        CancellationToken ct = default);

    Task<ReusableIngestionConfiguration> SaveExtractedAsync(
        ReusableIngestionConfiguration value,
        CancellationToken ct = default);
}
