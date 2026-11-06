// 验证版本化工艺配置的原子状态转换和数据库引用完整性。

using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresProcessConfigurationStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task StaleDraftWrite_CannotRevertPublishedDataModel()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessConfigurationStore(postgres.DataSource);
        var id = $"atomic-model-{Guid.NewGuid():N}";
        var draft = DataModel(id, "draft");

        Assert.True((await store.TryUpsertDataModelAsync(draft)).Succeeded);
        Assert.True((await store.TryUpsertDataModelAsync(draft with
        {
            Name = "published",
            Status = ConfigurationStatuses.Published,
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        })).Succeeded);

        var stale = await store.TryUpsertDataModelAsync(draft with
        {
            Name = "stale save",
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(2)
        });

        Assert.Equal(ProcessConfigurationMutationStatus.StateConflict, stale.Status);
        var stored = await store.GetDataModelAsync(id, 1);
        Assert.Equal(ConfigurationStatuses.Published, stored!.Status);
        Assert.Equal("published", stored.Name);
    }

    [LinuxDockerFact]
    public async Task DraftDataModelDelete_IsRejectedWhenReferencedByAnalysisPlan()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessConfigurationStore(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var model = DataModel($"referenced-model-{suffix}", "draft");
        Assert.True((await store.TryUpsertDataModelAsync(model)).Succeeded);
        Assert.True((await store.TryUpsertAnalysisPlanAsync(new ProcessAnalysisPlan
        {
            PlanId = $"referencing-plan-{suffix}",
            Version = 1,
            Name = "Referencing plan",
            Status = ConfigurationStatuses.Draft,
            DataModelId = model.ModelId,
            DataModelVersion = model.Version,
            UpdatedAt = DateTimeOffset.UtcNow
        })).Succeeded);

        var deleted = await store.TryDeleteDataModelAsync(model.ModelId, model.Version);

        Assert.Equal(ProcessConfigurationMutationStatus.Referenced, deleted.Status);
        Assert.NotNull(await store.GetDataModelAsync(model.ModelId, model.Version));
    }

    [LinuxDockerFact]
    public async Task DraftAnalysisPlanDelete_IsRejectedWhenReferencedByScenarioPackage()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessConfigurationStore(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var model = DataModel($"scenario-model-{suffix}", "draft");
        var plan = new ProcessAnalysisPlan
        {
            PlanId = $"scenario-plan-{suffix}",
            Version = 1,
            Name = "Scenario plan",
            Status = ConfigurationStatuses.Draft,
            DataModelId = model.ModelId,
            DataModelVersion = model.Version,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Assert.True((await store.TryUpsertDataModelAsync(model)).Succeeded);
        Assert.True((await store.TryUpsertAnalysisPlanAsync(plan)).Succeeded);
        Assert.True((await store.TryUpsertScenarioPackageAsync(new ScenarioPackage
        {
            PackageId = $"scenario-package-{suffix}",
            Version = 1,
            Name = "Scenario package",
            Status = ConfigurationStatuses.Draft,
            DataModelId = model.ModelId,
            DataModelVersion = model.Version,
            AnalysisPlanId = plan.PlanId,
            AnalysisPlanVersion = plan.Version,
            UpdatedAt = DateTimeOffset.UtcNow
        })).Succeeded);

        var deleted = await store.TryDeleteAnalysisPlanAsync(plan.PlanId, plan.Version);

        Assert.Equal(ProcessConfigurationMutationStatus.Referenced, deleted.Status);
        Assert.NotNull(await store.GetAnalysisPlanAsync(plan.PlanId, plan.Version));
    }

    private static ProcessDataModel DataModel(string id, string status) => new()
    {
        ModelId = id,
        Version = 1,
        Name = "Atomic model",
        Status = status,
        Acquisition = new AcquisitionModel(),
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
