using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresIngestionConfigurationStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task PublishedTemplateAndDataSourceVersionsAreImmutable()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresIngestionConfigurationStore(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var template = Template($"template-{suffix}");
        var source = Source($"source-{suffix}");

        await store.PublishTemplateExclusiveAsync(template);
        await store.PublishDataSourceExclusiveAsync(source);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PublishTemplateExclusiveAsync(template with { Name = "replacement" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PublishDataSourceExclusiveAsync(source with { Name = "replacement" }));
        Assert.False(await store.DeleteTemplateAsync(template.TemplateId, template.Version));
        Assert.False(await store.DeleteDataSourceAsync(source.DataSourceId, source.Version));
        Assert.Equal("template", (await store.GetTemplateAsync(template.TemplateId, 1))!.Name);
        Assert.Equal("source", (await store.GetDataSourceAsync(source.DataSourceId, 1))!.Name);
    }

    private static IngestionTaskTemplate Template(string id) => new()
    {
        TemplateId = id,
        Name = "template",
        Status = ConfigurationStatuses.Published,
        Protocol = AcquisitionProtocols.HttpPolling,
        DataModelId = "model",
        ValueMappings = [new AcquisitionValueMapping { DataItemCode = "value", SourcePath = "value" }]
    };

    private static DataSourceInstance Source(string id) => new()
    {
        DataSourceId = id,
        Name = "source",
        Status = ConfigurationStatuses.Published,
        EdgeId = "EDGE-001",
        Protocol = AcquisitionProtocols.HttpPolling,
        SourceKey = $"connector/{id}",
        SubjectId = id,
        HttpPolling = new HttpPollingConnection { BaseUrl = "http://device.local" }
    };
}
