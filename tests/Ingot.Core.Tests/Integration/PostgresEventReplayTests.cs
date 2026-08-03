using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresEventReplayTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ReplayedOutboxEvent_ShouldBeAcknowledgedWithoutDuplicateBusinessEvent()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        await using var manufacturing = new PostgresManufacturingContextStore(postgres.Configuration);
        await using var configurations = new PostgresProcessConfigurationStore(postgres.Configuration);
        await using var materializations = new PostgresCycleAnalysisMaterializationStore(
            postgres.Configuration,
            NullLogger<PostgresCycleAnalysisMaterializationStore>.Instance);
        await using var timeSeries = new PostgresTimeSeriesStore(
            postgres.Configuration,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        await using var store = new PostgresPlatformEventStore(
            postgres.Configuration,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            new CycleAnalysisRecomputeQueue(),
            timeSeries);

        var edgeId = $"EDGE-REPLAY-{Guid.NewGuid():N}";
        var evt = ProductionEvent.Create(
            "equipment.heartbeat",
            DateTimeOffset.UtcNow,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01")) with
        {
            Seq = 1
        };
        var request = new EventBatchRequest { EdgeId = edgeId, Events = [evt] };

        var first = await store.IngestAsync(request);
        var replay = await store.IngestAsync(request);

        Assert.Equal(1, first.Accepted);
        Assert.Equal(0, first.Duplicates);
        Assert.Equal(1, first.AckSeq);
        Assert.Equal(0, replay.Accepted);
        Assert.Equal(1, replay.Duplicates);
        Assert.Equal(1, replay.AckSeq);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM production_events WHERE event_id = @event_id;",
            connection);
        count.Parameters.AddWithValue("event_id", evt.EventId);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [LinuxDockerFact]
    public async Task AcquisitionModel_ShouldRemainAuthoritative_WhenRecipeReferencesOlderModel()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        await using var manufacturing = new PostgresManufacturingContextStore(postgres.Configuration);
        await using var configurations = new PostgresProcessConfigurationStore(postgres.Configuration);
        await using var materializations = new PostgresCycleAnalysisMaterializationStore(
            postgres.Configuration,
            NullLogger<PostgresCycleAnalysisMaterializationStore>.Instance);
        await using var timeSeries = new PostgresTimeSeriesStore(
            postgres.Configuration,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        await using var store = new PostgresPlatformEventStore(
            postgres.Configuration,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            new CycleAnalysisRecomputeQueue(),
            timeSeries);

        var suffix = Guid.NewGuid().ToString("N");
        var modelId = $"model-{suffix}";
        var recipeId = $"recipe-{suffix}";
        var planId = $"plan-{suffix}";
        var edgeId = $"EDGE-MODEL-{suffix}";
        var correlationId = Guid.CreateVersion7().ToString();
        var now = DateTimeOffset.UtcNow;
        var temperature = new ProcessDataItemDefinition
        {
            Code = "temperature.actual",
            SourceField = "实际温度",
            DataType = "double",
            Unit = "Cel",
            Nullable = false
        };
        var stage = new ProcessDataItemDefinition
        {
            Code = "process.stage_number",
            SourceField = "阶段号",
            DataType = "integer",
            Category = "stage",
            Nullable = false
        };
        var recipeParameter = new RecipeParameterDefinition
        {
            Code = "recipe.temperature_setpoint",
            SourceField = "温度设定",
            DataType = "double",
            Unit = "Cel",
            Nullable = false
        };
        await configurations.UpsertDataModelAsync(new ProcessDataModel
        {
            ModelId = modelId,
            Version = 1,
            Name = "旧数据模型",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel { DataItems = [temperature] },
            RecipeParameters = [recipeParameter],
            UpdatedAt = now
        });
        await configurations.UpsertDataModelAsync(new ProcessDataModel
        {
            ModelId = modelId,
            Version = 2,
            Name = "采集数据模型",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel { DataItems = [temperature, stage] },
            RecipeParameters = [recipeParameter],
            UpdatedAt = now
        });
        await configurations.UpsertRecipeVersionAsync(new RecipeVersion
        {
            RecipeId = recipeId,
            Version = 1,
            Name = "历史配方",
            DataModelId = modelId,
            DataModelVersion = 1,
            Status = ConfigurationStatuses.Published,
            Values =
            [
                new RecipeParameterValue
                {
                    Code = recipeParameter.Code,
                    Value = JsonSerializer.SerializeToElement(620d)
                }
            ],
            UpdatedAt = now
        });
        foreach (var version in new[] { 1, 2 })
        {
            await configurations.UpsertAnalysisPlanAsync(new ProcessAnalysisPlan
            {
                PlanId = planId,
                Version = version,
                Name = $"分析计划 {version}",
                Status = ConfigurationStatuses.Published,
                DataModelId = modelId,
                DataModelVersion = version,
                Signals =
                [
                    new AnalysisSignalSelection
                    {
                        DataItemCode = temperature.Code,
                        Features = ["mean"]
                    }
                ],
                UpdatedAt = now
            });
        }

        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["data_model_id"] = modelId,
            ["data_model_version"] = "2",
            ["recipe_id"] = recipeId,
            ["recipe_version"] = "1",
            ["equipment_id"] = "PRESS-01"
        };
        var started = ProductionEvent.Create(
            "cycle.started",
            now,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            correlationId,
            context) with { Seq = 1 };
        var sample = ProductionEvent.Create(
            "process.sample",
            now.AddSeconds(1),
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            correlationId,
            context,
            new Dictionary<string, object?>
            {
                ["values"] = new Dictionary<string, object?>
                {
                    [temperature.Code] = 618.5d,
                    [stage.Code] = 20L
                }
            }) with { Seq = 2 };

        var response = await store.IngestAsync(new EventBatchRequest
        {
            EdgeId = edgeId,
            Events = [started, sample]
        });

        Assert.Equal(2, response.Accepted);
        Assert.Equal(2, response.AckSeq);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT context::text, data::text FROM production_events WHERE event_id = @event_id;",
            connection);
        command.Parameters.AddWithValue("event_id", started.EventId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        using var storedContext = JsonDocument.Parse(reader.GetString(0));
        using var storedData = JsonDocument.Parse(reader.GetString(1));
        Assert.Equal("2", storedContext.RootElement.GetProperty("data_model_version").GetString());
        Assert.Equal("1", storedContext.RootElement.GetProperty("recipe_data_model_version").GetString());
        Assert.Equal("model_mismatch", storedContext.RootElement.GetProperty("recipe_snapshot_status").GetString());
        Assert.True(storedData.RootElement.TryGetProperty("plannedRecipeParameters", out _));
        Assert.False(storedData.RootElement.TryGetProperty("recipeParameters", out _));
    }
}
