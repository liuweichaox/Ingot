// 验证 PostgresEventReplay 的真实基础设施集成、失败和恢复行为。

using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessExecutions;
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
    public async Task BoundaryProjection_ShouldCoalesceBatchesAndCompleteFromCanonicalEvent()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        var manufacturing = new PostgresManufacturingContextStore(postgres.DataSource);
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        var materializations = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        using var events = new PostgresPlatformEventStore(
            postgres.DataSource,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            timeSeries);
        var boundaries = new PostgresExecutionBoundaryStore(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var siteId = $"SITE-BOUNDARY-{suffix}";
        var edgeId = $"EDGE-BOUNDARY-{suffix}";
        var executionId = $"execution-{suffix}";
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var started = ProductionEvent.Create(
            "process.execution.started",
            startedAt,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            executionId) with
        { Seq = 1 };
        var completed = ProductionEvent.Create(
            "process.execution.completed",
            startedAt.AddMinutes(1),
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            executionId) with
        { Seq = 2 };

        await events.IngestAsync(new EventBatchRequest
        {
            SiteId = siteId,
            EdgeId = edgeId,
            Events = [started]
        });
        await events.IngestAsync(new EventBatchRequest
        {
            SiteId = siteId,
            EdgeId = edgeId,
            Events = [completed]
        });

        ExecutionBoundaryProjectionResult? projected = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var lease = await boundaries.ClaimProjectionAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
            Assert.NotNull(lease);
            var candidate = await boundaries.ProjectAsync(lease, TimeSpan.FromHours(10), CancellationToken.None);
            Assert.True(await boundaries.FinishProjectionAsync(lease, candidate?.RecheckAt, CancellationToken.None));
            if (lease.SiteId == siteId && lease.SourceExecutionId == executionId)
            {
                projected = candidate;
                break;
            }
        }
        Assert.NotNull(projected);

        var boundary = Assert.IsType<ExecutionBoundary>(
            await boundaries.GetBoundaryAsync(siteId, executionId, CancellationToken.None));
        Assert.Equal(ExecutionBoundaryStatus.Completed, boundary.Status);
        Assert.Equal(ExecutionBoundaryConfidence.Complete, boundary.Confidence);
        Assert.Equal(2, boundary.EventCount);
        Assert.Equal(startedAt, boundary.StartedAt);
        Assert.Equal(startedAt.AddMinutes(1), boundary.EndedAt);
        Assert.True(boundary.MinIngestId < boundary.MaxIngestId);
        Assert.False(boundary.GapDetected);
    }

    [LinuxDockerFact]
    public async Task ReplayedOutboxEvent_ShouldBeAcknowledgedWithoutDuplicateBusinessEvent()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        var manufacturing = new PostgresManufacturingContextStore(postgres.DataSource);
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        var materializations = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        using var store = new PostgresPlatformEventStore(
            postgres.DataSource,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            timeSeries);

        var edgeId = $"EDGE-REPLAY-{Guid.NewGuid():N}";
        var evt = ProductionEvent.Create(
            "equipment.heartbeat",
            DateTimeOffset.UtcNow,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            appliedConfiguration: new AppliedConfigurationRef("ingestion-task", "TASK-REPLAY", 4),
            qualityFlags: ["communication_degraded"]) with
        {
            Seq = 1
        };
        var request = new EventBatchRequest { SiteId = "SITE-REPLAY", EdgeId = edgeId, Events = [evt] };

        var first = await store.IngestAsync(request);
        var replay = await store.IngestAsync(request);

        var conflicting = ProductionEventIntegrity.Seal(evt with
        {
            Data = new Dictionary<string, object?> { ["changed"] = true }
        });
        var conflict = await Assert.ThrowsAsync<EventIngestConflictException>(() => store.IngestAsync(
            request with { Events = [conflicting] }));

        Assert.Equal(1, first.Accepted);
        Assert.Equal(0, first.Duplicates);
        Assert.Equal(1, first.AckSeq);
        Assert.Equal(0, replay.Accepted);
        Assert.Equal(1, replay.Duplicates);
        Assert.Equal(1, replay.AckSeq);
        Assert.Contains("载荷冲突", conflict.Message, StringComparison.Ordinal);
        var stored = Assert.Single(await store.QueryAsync(new PlatformEventQuery
        {
            SiteId = "SITE-REPLAY",
            EdgeId = edgeId
        }));
        Assert.Equal("SITE-REPLAY", stored.SiteId);
        Assert.Equal(1, stored.Event.SchemaVersion);
        Assert.Equal(new AppliedConfigurationRef("ingestion-task", "TASK-REPLAY", 4), stored.Event.AppliedConfiguration);
        Assert.Equal(["communication_degraded"], stored.Event.QualityFlags);
        Assert.True(ProductionEventIntegrity.HasValidPayloadHash(stored.Event));
        Assert.Empty(await store.QueryAsync(new PlatformEventQuery
        {
            SiteId = "SITE-OTHER",
            EdgeId = edgeId
        }));
        var dataObject = Assert.Single((await store.QueryDataObjectsAsync(new DataObjectQuery
        {
            SiteId = "SITE-REPLAY",
            SubjectType = "equipment",
            SubjectId = "PRESS-01"
        })).Data);
        Assert.Equal("SITE-REPLAY", dataObject.SiteId);
        Assert.Empty((await store.QueryDataObjectsAsync(new DataObjectQuery
        {
            SiteId = "SITE-OTHER",
            SubjectType = "equipment",
            SubjectId = "PRESS-01"
        })).Data);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM production_events WHERE event_id = @event_id;",
            connection);
        count.Parameters.AddWithValue("event_id", evt.EventId);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [LinuxDockerFact]
    public async Task EqualExecutionIdentifiersAcrossSites_ShouldKeepSeparateOperationContexts()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        var manufacturing = new PostgresManufacturingContextStore(postgres.DataSource);
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        var materializations = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        using var store = new PostgresPlatformEventStore(
            postgres.DataSource,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            timeSeries);
        var suffix = Guid.NewGuid().ToString("N");
        var executionId = $"shared-execution-{suffix}";
        var siteA = $"SITE-A-{suffix}";
        var siteB = $"SITE-B-{suffix}";
        var edgeA = $"EDGE-A-{suffix}";
        var edgeB = $"EDGE-B-{suffix}";
        var now = DateTimeOffset.UtcNow;

        static ProductionEvent LifecycleEvent(
            string eventType,
            string executionId,
            string edgeId,
            DateTimeOffset occurredAt,
            long sequence,
            string? marker = null)
            => ProductionEvent.Create(
                eventType,
                occurredAt,
                $"edge/{edgeId}/equipment/PRESS-01",
                new ObjectRef("equipment", "PRESS-01"),
                executionId,
                marker is null
                    ? null
                    : new Dictionary<string, string> { ["site_marker"] = marker }) with
            { Seq = sequence };

        await store.IngestAsync(new EventBatchRequest
        {
            SiteId = siteA,
            EdgeId = edgeA,
            Events = [LifecycleEvent("process.execution.started", executionId, edgeA, now, 1, "A")]
        });
        await store.IngestAsync(new EventBatchRequest
        {
            SiteId = siteB,
            EdgeId = edgeB,
            Events = [LifecycleEvent("process.execution.started", executionId, edgeB, now, 1, "B")]
        });
        await store.IngestAsync(new EventBatchRequest
        {
            SiteId = siteA,
            EdgeId = edgeA,
            Events = [LifecycleEvent("process.execution.completed", executionId, edgeA, now.AddMinutes(1), 2)]
        });
        await store.IngestAsync(new EventBatchRequest
        {
            SiteId = siteB,
            EdgeId = edgeB,
            Events = [LifecycleEvent("process.execution.completed", executionId, edgeB, now.AddMinutes(1), 2)]
        });

        var siteAEvents = await store.QueryByExecutionIdsAsync([executionId], siteA);
        var siteBEvents = await store.QueryByExecutionIdsAsync([executionId], siteB);
        Assert.All(siteAEvents, row => Assert.Equal(siteA, row.SiteId));
        Assert.All(siteBEvents, row => Assert.Equal(siteB, row.SiteId));
        Assert.Equal(
            "A",
            siteAEvents.Single(row => row.Event.EventType == "process.execution.completed")
                .Event.Context["site_marker"]);
        Assert.Equal(
            "B",
            siteBEvents.Single(row => row.Event.EventType == "process.execution.completed")
                .Event.Context["site_marker"]);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM operation_context_snapshots WHERE execution_id = @execution_id;",
            connection);
        count.Parameters.AddWithValue("execution_id", executionId);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    [LinuxDockerFact]
    public async Task ProcessSample_ShouldPersistAndQueryOnlyTypedValues()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        var manufacturing = new PostgresManufacturingContextStore(postgres.DataSource);
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        var materializations = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        using var store = new PostgresPlatformEventStore(
            postgres.DataSource,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            timeSeries);

        var suffix = Guid.NewGuid().ToString("N");
        var modelId = $"single-source-model-{suffix}";
        var executionId = $"single-source-execution-{suffix}";
        var edgeId = $"EDGE-SINGLE-SOURCE-{suffix}";
        var now = DateTimeOffset.UtcNow;
        var signals = Enumerable.Range(1, 10)
            .Select(index => ($"sensor.{index:00}", Value: 600d + index))
            .ToArray();
        await configurations.UpsertDataModelAsync(new ProcessDataModel
        {
            ModelId = modelId,
            Version = 1,
            Name = "单一事实源模型",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems = signals.Select(signal => new ProcessDataItemDefinition
                {
                    Code = signal.Item1,
                    DisplayName = signal.Item1,
                    DataType = "double",
                    Nullable = false
                }).ToArray()
            },
            UpdatedAt = now
        });
        await configurations.UpsertAnalysisPlanAsync(new ProcessAnalysisPlan
        {
            PlanId = $"single-source-plan-{suffix}",
            Version = 1,
            Name = "单一事实源分析",
            Status = ConfigurationStatuses.Published,
            DataModelId = modelId,
            DataModelVersion = 1,
            UpdatedAt = now
        });

        var sample = ProductionEvent.Create(
            "process.sample",
            now,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            executionId,
            new Dictionary<string, string>
            {
                ["data_model_id"] = modelId,
                ["data_model_version"] = "1"
            },
            new Dictionary<string, object?>
            {
                ["values"] = signals.ToDictionary(
                    static signal => signal.Item1,
                    static signal => (object?)signal.Value,
                    StringComparer.Ordinal)
            }) with
        { Seq = 1 };

        var first = await store.IngestAsync(new EventBatchRequest
        {
            SiteId = "SITE-SINGLE-SOURCE",
            EdgeId = edgeId,
            Events = [sample]
        });
        var replay = await store.IngestAsync(new EventBatchRequest
        {
            SiteId = "SITE-SINGLE-SOURCE",
            EdgeId = edgeId,
            Events = [sample]
        });

        Assert.Equal(1, first.Accepted);
        Assert.Equal(1, replay.Duplicates);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT
                           (SELECT count(*) FROM production_events WHERE event_id = @event_id),
                           (SELECT count(*) FROM process_sample_frames WHERE event_id = @event_id),
                           (SELECT count(*) FROM process_sample_values AS value
                            JOIN process_sample_frames AS frame
                              ON frame.frame_id = value.frame_id AND frame.occurred_at = value.occurred_at
                            WHERE frame.event_id = @event_id);
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("event_id", sample.EventId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.Equal(10, reader.GetInt64(2));
        }

        Assert.Empty(await store.QueryAsync(new PlatformEventQuery
        {
            ExecutionId = executionId,
            EventType = "process.sample"
        }));
        var typed = await timeSeries.QueryAsync(new TimeSeriesQuery
        {
            SiteId = "SITE-SINGLE-SOURCE",
            ExecutionId = executionId
        });
        Assert.Equal(10, typed.Count);
        Assert.All(typed, static row => Assert.Equal("SITE-SINGLE-SOURCE", row.SiteId));
        Assert.Empty(await timeSeries.QueryAsync(new TimeSeriesQuery
        {
            SiteId = "SITE-OTHER",
            ExecutionId = executionId
        }));
        Assert.Equal(601d, typed.Single(static row => row.SignalCode == "sensor.01").NumericValue);
        var frames = await timeSeries.QueryFramesAsync(new TimeSeriesQuery
        {
            ExecutionId = executionId,
            Limit = 1
        });
        var frame = Assert.Single(frames);
        Assert.Equal(10, frame.NumericValues.Count);
        Assert.Equal(610d, frame.NumericValues["sensor.10"]);
    }

    [LinuxDockerFact]
    public async Task AcquisitionModel_ShouldRemainAuthoritative_WhenProcessSpecificationReferencesOlderModel()
    {
        await postgres.EnsureSchemaAsync();
        var options = Options.Create(new PlatformEventOptions());
        var manufacturing = new PostgresManufacturingContextStore(postgres.DataSource);
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        var materializations = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            options);
        using var store = new PostgresPlatformEventStore(
            postgres.DataSource,
            NullLogger<PostgresPlatformEventStore>.Instance,
            new PlatformEventMetrics(),
            options,
            manufacturing,
            new ProcessAnalysisResolver(configurations),
            materializations,
            timeSeries);

        var suffix = Guid.NewGuid().ToString("N");
        var modelId = $"model-{suffix}";
        var processSpecificationId = $"processSpecification-{suffix}";
        var planId = $"plan-{suffix}";
        var edgeId = $"EDGE-MODEL-{suffix}";
        var executionId = Guid.CreateVersion7().ToString();
        var now = DateTimeOffset.UtcNow;
        var temperature = new ProcessDataItemDefinition
        {
            Code = "temperature.actual",
            DisplayName = "实际温度",
            DataType = "double",
            Unit = "Cel",
            Nullable = false
        };
        var stage = new ProcessDataItemDefinition
        {
            Code = "process.stage_number",
            DisplayName = "阶段号",
            DataType = "integer",
            Category = "stage",
            Nullable = false
        };
        var controlParameter = new ControlParameterDefinition
        {
            Code = "processSpecification.temperature_setpoint",
            DisplayName = "温度设定",
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
            ControlParameters = [controlParameter],
            UpdatedAt = now
        });
        await configurations.UpsertDataModelAsync(new ProcessDataModel
        {
            ModelId = modelId,
            Version = 2,
            Name = "采集数据模型",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel { DataItems = [temperature, stage] },
            ControlParameters = [controlParameter],
            UpdatedAt = now
        });
        await configurations.UpsertProcessSpecificationAsync(new ProcessSpecification
        {
            ProcessSpecificationId = processSpecificationId,
            Version = 1,
            Name = "历史工艺规范",
            DataModelId = modelId,
            DataModelVersion = 1,
            Status = ConfigurationStatuses.Published,
            Values =
            [
                new ControlParameterValue
                {
                    Code = controlParameter.Code,
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
            ["process_specification_id"] = processSpecificationId,
            ["process_specification_version"] = "1",
            ["equipment_id"] = "PRESS-01"
        };
        var started = ProductionEvent.Create(
            "process.execution.started",
            now,
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            executionId,
            context) with
        { Seq = 1 };
        var sample = ProductionEvent.Create(
            "process.sample",
            now.AddSeconds(1),
            $"edge/{edgeId}/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01"),
            executionId,
            context,
            new Dictionary<string, object?>
            {
                ["values"] = new Dictionary<string, object?>
                {
                    [temperature.Code] = 618.5d,
                    [stage.Code] = 20L
                }
            }) with
        { Seq = 2 };

        var response = await store.IngestAsync(new EventBatchRequest
        {
            SiteId = "SITE-CONTEXT",
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
        Assert.Equal("1", storedContext.RootElement.GetProperty("process_specification_data_model_version").GetString());
        Assert.Equal("model_mismatch", storedContext.RootElement.GetProperty("process_specification_snapshot_status").GetString());
        Assert.True(storedData.RootElement.TryGetProperty("plannedControlParameterValues", out _));
        Assert.False(storedData.RootElement.TryGetProperty("controlParameters", out _));
    }
}
