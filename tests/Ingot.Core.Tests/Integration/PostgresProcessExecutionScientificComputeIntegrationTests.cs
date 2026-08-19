using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresProcessExecutionScientificComputeIntegrationTests(
    PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task LargeOffsetSmallVariation_ShouldMatchDeterministicReference()
    {
        await postgres.EnsureSchemaAsync();
        using var timeSeries = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            Options.Create(new PlatformEventOptions()));
        await timeSeries.InitializeAsync();

        var executionId = $"scientific-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt.AddSeconds(3);
        double[] values =
        [
            1_000_000_000.000,
            1_000_000_000.003,
            999_999_999.998,
            1_000_000_000.004
        ];
        var rows = values.Select((value, index) => new ProcessSampleFrame
        {
            EventId = $"scientific-event-{index}",
            IngestId = index + 1,
            OccurredAt = startedAt.AddSeconds(index),
            RecordedAt = startedAt.AddSeconds(index),
            IngestedAt = startedAt.AddSeconds(index).AddMilliseconds(5),
            NumericValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["signal.large-offset"] = value
            }
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
                        DisplayName = "Large-offset signal",
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
        var reference = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            startedAt,
            completedAt,
            model,
            plan);
        var expectedStandardDeviation = reference.Signals.Single().Features
            .Single(static feature => feature.Code == "stddev").Value;
        Assert.NotNull(expectedStandardDeviation);
        Assert.True(expectedStandardDeviation > 0.001);

        await InsertSamplesAsync(executionId, startedAt, values);
        var databaseEngine = new PostgresProcessExecutionScientificComputeEngine(
            postgres.DataSource);

        var verified = await databaseEngine.ComputeAndVerifyAsync(
            executionId,
            startedAt,
            completedAt,
            reference);

        var actualStandardDeviation = verified.Signals.Single().Features
            .Single(static feature => feature.Code == "stddev").Value;
        Assert.Equal(expectedStandardDeviation!.Value, actualStandardDeviation!.Value, 10);
    }

    private async Task InsertSamplesAsync(
        string executionId,
        DateTimeOffset startedAt,
        IReadOnlyList<double> values)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        for (var index = 0; index < values.Count; index++)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO signal_definitions (
                  data_model_id, data_model_version, signal_code, source_field, data_type,
                  unit, category, definition_hash, first_seen_at, last_seen_at)
                VALUES (
                  'scientific-model', 1, @signal_code, @signal_code, 'double',
                  'V', 'process', 'scientific-test', @occurred_at, @occurred_at)
                ON CONFLICT (data_model_id, data_model_version, signal_code) DO NOTHING;

                INSERT INTO collection_points (
                  collection_point_id, site_id, edge_id, subject_type, subject_id, signal_code,
                  first_seen_at, last_seen_at)
                VALUES (
                  @point_id, 'SITE-SCIENTIFIC', @edge_id, 'equipment', 'PRESS-01', @signal_code,
                  @occurred_at, @occurred_at)
                ON CONFLICT (collection_point_id) DO NOTHING;

                INSERT INTO process_sample_frames (
                  occurred_at, frame_id, event_id, recorded_at, ingested_at, site_id, edge_id,
                  source, subject_type, subject_id, execution_id, data_model_id, data_model_version)
                VALUES (
                  @occurred_at, @ingest_id, @event_id, @recorded_at, @recorded_at, 'SITE-SCIENTIFIC', @edge_id,
                  @source, 'equipment', 'PRESS-01', @execution_id, 'scientific-model', 1);

                INSERT INTO process_sample_values (
                  occurred_at, frame_id, point_key, quality_code, numeric_value)
                VALUES (
                  @occurred_at, @ingest_id,
                  (SELECT point_key FROM collection_points WHERE collection_point_id = @point_id),
                  0, @numeric_value);
                """,
                connection);
            command.Parameters.AddWithValue("occurred_at", startedAt.AddSeconds(index).UtcDateTime);
            command.Parameters.AddWithValue("point_id", "SITE-SCIENTIFIC/EDGE-SCIENTIFIC/equipment/PRESS-01/signal.large-offset");
            command.Parameters.AddWithValue("signal_code", "signal.large-offset");
            command.Parameters.AddWithValue("numeric_value", values[index]);
            command.Parameters.AddWithValue("event_id", $"{executionId}-{index}");
            command.Parameters.AddWithValue("ingest_id", index + 1L);
            command.Parameters.AddWithValue("recorded_at", startedAt.AddSeconds(index).UtcDateTime);
            command.Parameters.AddWithValue("edge_id", "EDGE-SCIENTIFIC");
            command.Parameters.AddWithValue("source", "integration-test");
            command.Parameters.AddWithValue("execution_id", executionId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
