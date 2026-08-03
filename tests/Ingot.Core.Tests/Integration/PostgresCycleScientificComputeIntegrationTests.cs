using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresCycleScientificComputeIntegrationTests(
    PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task LargeOffsetSmallVariation_ShouldMatchDeterministicReference()
    {
        await postgres.EnsureSchemaAsync();
        await using var timeSeries = new PostgresTimeSeriesStore(
            postgres.Configuration,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            Options.Create(new PlatformEventOptions()));
        await timeSeries.InitializeAsync();

        var correlationId = $"scientific-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt.AddSeconds(3);
        double[] values =
        [
            1_000_000_000.000,
            1_000_000_000.003,
            999_999_999.998,
            1_000_000_000.004
        ];
        var rows = values.Select((value, index) => new PlatformProductionEvent
        {
            IngestId = index + 1,
            EdgeId = "EDGE-SCIENTIFIC",
            IngestedAt = startedAt.AddSeconds(index).AddMilliseconds(5),
            Event = ProductionEvent.Create(
                "process.sample",
                startedAt.AddSeconds(index),
                "edge/EDGE-SCIENTIFIC/equipment/PRESS-01",
                new ObjectRef("equipment", "PRESS-01"),
                correlationId,
                data: new Dictionary<string, object?>
                {
                    ["values"] = new Dictionary<string, object?>
                    {
                        ["signal.large-offset"] = value
                    }
                })
        }).ToArray();
        var model = new ProcessDataModel
        {
            ModelId = "scientific-model",
            Version = 1,
            Name = "Scientific model",
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "signal.large-offset",
                        SourceField = "Large-offset signal",
                        DataType = "double",
                        Unit = "V",
                        Nullable = false
                    }
                ]
            }
        };
        var plan = new ProcessAnalysisPlan
        {
            PlanId = "scientific-plan",
            Version = 1,
            Name = "Scientific plan",
            DataModelId = model.ModelId,
            DataModelVersion = model.Version,
            Signals =
            [
                new AnalysisSignalSelection
                {
                    DataItemCode = "signal.large-offset",
                    Features = ["mean", "stddev"]
                }
            ]
        };
        var reference = new WholeCycleAnalysisEngine().Analyze(
            rows,
            startedAt,
            completedAt,
            model,
            plan);
        var expectedStandardDeviation = reference.Signals.Single().Features
            .Single(static feature => feature.Code == "stddev").Value;
        Assert.NotNull(expectedStandardDeviation);
        Assert.True(expectedStandardDeviation > 0.001);

        await InsertSamplesAsync(correlationId, startedAt, values);
        await using var databaseEngine = new PostgresCycleScientificComputeEngine(
            postgres.Configuration);

        var verified = await databaseEngine.ComputeAndVerifyAsync(
            correlationId,
            startedAt,
            completedAt,
            reference);

        var actualStandardDeviation = verified.Signals.Single().Features
            .Single(static feature => feature.Code == "stddev").Value;
        Assert.Equal(expectedStandardDeviation!.Value, actualStandardDeviation!.Value, 10);
    }

    private async Task InsertSamplesAsync(
        string correlationId,
        DateTimeOffset startedAt,
        IReadOnlyList<double> values)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        for (var index = 0; index < values.Count; index++)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO time_series_samples (
                  occurred_at, collection_point_id, signal_code, data_type, unit, category,
                  numeric_value, quality_code, event_id, ingest_id, recorded_at, edge_id,
                  source, subject_type, subject_id, correlation_id, data_model_id,
                  data_model_version, run_context)
                VALUES (
                  @occurred_at, @point_id, @signal_code, 'double', 'V', 'process',
                  @numeric_value, 'good', @event_id, @ingest_id, @recorded_at, @edge_id,
                  @source, 'equipment', 'PRESS-01', @correlation_id, 'scientific-model',
                  1, '{}'::jsonb);
                """,
                connection);
            command.Parameters.AddWithValue("occurred_at", startedAt.AddSeconds(index).UtcDateTime);
            command.Parameters.AddWithValue("point_id", "EDGE-SCIENTIFIC/equipment/PRESS-01/signal.large-offset");
            command.Parameters.AddWithValue("signal_code", "signal.large-offset");
            command.Parameters.AddWithValue("numeric_value", values[index]);
            command.Parameters.AddWithValue("event_id", $"{correlationId}-{index}");
            command.Parameters.AddWithValue("ingest_id", index + 1L);
            command.Parameters.AddWithValue("recorded_at", startedAt.AddSeconds(index).UtcDateTime);
            command.Parameters.AddWithValue("edge_id", "EDGE-SCIENTIFIC");
            command.Parameters.AddWithValue("source", "integration-test");
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
