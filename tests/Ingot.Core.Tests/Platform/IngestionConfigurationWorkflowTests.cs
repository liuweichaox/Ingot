// 验证平台组件 IngestionConfigurationWorkflow 的成功、拒绝和安全边界。

using Ingot.Contracts.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class IngestionConfigurationWorkflowTests
{
    [Fact]
    public async Task ImportDataSources_NormalizesAndSavesThroughApplicationWorkflow()
    {
        var store = new DataSourceStore();
        var workflow = new IngestionConfigurationWorkflow(store, null!, null!, null!);

        var saved = await workflow.ImportDataSourcesAsync([Source(" Press-01 ")]);

        Assert.Single(saved);
        Assert.Equal("press-01", saved[0].DataSourceId);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task ImportDataSources_RejectsDuplicateBatchWithoutWriting()
    {
        var store = new DataSourceStore();
        var workflow = new IngestionConfigurationWorkflow(store, null!, null!, null!);

        var error = await Assert.ThrowsAsync<AcquisitionWorkflowException>(() =>
            workflow.ImportDataSourcesAsync([Source("press-01"), Source("PRESS-01")]));

        Assert.Equal(AcquisitionWorkflowFailureKind.Invalid, error.Kind);
        Assert.Empty(store.Saved);
    }

    private static DataSourceInstance Source(string id) => new()
    {
        DataSourceId = id,
        Name = "Press 01",
        EdgeId = "EDGE-01",
        Protocol = AcquisitionProtocols.HttpPolling,
        SourceKey = "connector/http/press-01",
        SubjectId = "press-01",
        HttpPolling = new HttpPollingConnection { BaseUrl = "http://press-01.local" }
    };

    private sealed class DataSourceStore : IIngestionConfigurationStore
    {
        public List<DataSourceInstance> Saved { get; } = [];

        public Task<DataSourceInstance?> GetDataSourceAsync(
            string dataSourceId,
            int version,
            CancellationToken ct = default)
            => Task.FromResult<DataSourceInstance?>(null);

        public Task<IReadOnlyList<DataSourceInstance>> SaveDataSourcesAsync(
            IReadOnlyList<DataSourceInstance> values,
            CancellationToken ct = default)
        {
            Saved.AddRange(values);
            return Task.FromResult(values);
        }

        public Task<IReadOnlyList<IngestionTaskTemplate>> ListTemplatesAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTaskTemplate?> GetTemplateAsync(string templateId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTaskTemplate> UpsertTemplateAsync(IngestionTaskTemplate value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTaskTemplate> PublishTemplateExclusiveAsync(IngestionTaskTemplate value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteTemplateAsync(string templateId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<DataSourceInstance>> ListDataSourcesAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DataSourceInstance> UpsertDataSourceAsync(DataSourceInstance value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DataSourceInstance> PublishDataSourceExclusiveAsync(DataSourceInstance value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTaskBinding>> ListBindingsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTaskBinding?> GetBindingAsync(string taskId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTask>> SaveMaterializedTasksAsync(
            IReadOnlyList<(IngestionTaskBinding Binding, IngestionTask Task)> values,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ReusableIngestionConfiguration> SaveExtractedAsync(
            ReusableIngestionConfiguration value,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
