// 验证 PostgresMigration 的真实基础设施集成、失败和恢复行为。

using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Migrations;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresMigrationTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentRunners_ShouldApplyEveryMigrationExactlyOnce()
    {
        var first = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        var second = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);

        await Task.WhenAll(first.RunAsync(), second.RunAsync());
        await first.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*), count(DISTINCT version) FROM schema_version;",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var expected = typeof(MigrationRunner).Assembly.GetManifestResourceNames().LongCount(name =>
            name.Contains(".Migrations.sql.", StringComparison.Ordinal) &&
            name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
        Assert.True(expected > 0);
        Assert.Equal(expected, reader.GetInt64(0));
        Assert.Equal(reader.GetInt64(0), reader.GetInt64(1));
    }

    [LinuxDockerFact]
    public async Task RemovedWebhookSchema_ShouldNotRemainAfterMigrations()
    {
        var runner = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        await runner.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.webhook_subscriptions') IS NULL;",
            connection);
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task RetiredPlanningVocabulary_ShouldNotRemainInCurrentSchema()
    {
        await postgres.EnsureSchemaAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              to_regclass('public.' || 'research_' || 'retired_workflow_records') IS NULL
              AND to_regclass('public.' || 'recommendation_' || 'knowledge_usage') IS NULL
              AND to_regclass('public.research_historical_replay_reports') IS NULL
              AND to_regclass('public.research_' || 'transfer_assessments') IS NULL
              AND to_regclass('public.research_' || 'rollback_drills') IS NULL
              AND NOT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND lower(table_name) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND lower(column_name) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM pg_constraint constraint_item
                JOIN pg_namespace namespace
                  ON namespace.oid = constraint_item.connamespace
                WHERE namespace.nspname = 'public'
                  AND lower(constraint_item.conname) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM pg_proc routine
                JOIN pg_namespace namespace
                  ON namespace.oid = routine.pronamespace
                WHERE namespace.nspname = 'public'
                  AND (
                    lower(routine.proname) LIKE '%' || concat('exper', 'iment') || '%'
                    OR routine.proname = concat('reject_shadow_', 'recommendation_mutation')))
              AND NOT EXISTS (
                SELECT 1
                FROM process_research_audit
                WHERE lower(resource_type) LIKE '%' || concat('exper', 'iment') || '%'
                   OR lower(payload->>'resourceType') LIKE
                      '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM research_hypothesis_evidence
                WHERE lower(kind) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM mechanism_claim_evidence
                WHERE lower(evidence_kind) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM mechanism_claim_lifecycle_decisions
                WHERE lower(evidence_kind) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM research_recipe_recommendations
                WHERE payload ? concat('exper', 'imentId')
                   OR payload ? concat('exper', 'iment_id'))
              AND NOT EXISTS (
                SELECT 1
                FROM research_operating_regions
                WHERE payload ? concat('supportingExper', 'imentIds')
                   OR payload ? concat('supporting_exper', 'iment_ids')
                   OR payload ? 'supportingResultIds'
                   OR payload ? 'supporting_result_ids'
                   OR lower((payload->'evidence')::text) LIKE
                      '%' || concat('"kind": "exper', 'iment-result"') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM research_knowledge_claims
                WHERE lower((payload->'evidence')::text) LIKE
                      '%' || concat('"kind": "exper', 'iment-result"') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM pg_class
                WHERE relnamespace = 'public'::regnamespace
                  AND lower(relname) LIKE '%' || concat('exper', 'iment') || '%')
              AND NOT EXISTS (
                SELECT 1
                FROM research_evidence
                WHERE lower(kind) LIKE '%' || concat('exper', 'iment') || '%'
                   OR lower(resource_type) LIKE '%' || concat('exper', 'iment') || '%'
                   OR kind = concat('transfer-', 'assessment')
                   OR resource_type = concat('transfer-', 'assessment'))
              AND NOT EXISTS (
                SELECT 1
                FROM research_knowledge_claims
                WHERE payload ? 'transferAssessmentId'
                   OR payload ? 'transfer_assessment_id'
                   OR payload::text LIKE '%' || concat('transfer-', 'assessment') || '%');
            """,
            connection);

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task LegacyWorkflowCleanup_ShouldCleanPersistedRowsAndPreserveCanonicalUsage()
    {
        var schema = $"cleanup_{Guid.NewGuid():N}";
        var retiredTerm = "exper" + "iment";
        var oldUsageTable = "recommendation_" + "knowledge_usage";
        var retiredArchiveTable = "research_" + "retired_workflow_records";
        var replayTable = "research_historical_" + "replay_reports";

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        try
        {
            var setupSql =
                $"""
                CREATE SCHEMA {schema};
                SET search_path TO {schema};

                CREATE TABLE {oldUsageTable} (
                    recommendation_id uuid NOT NULL,
                    claim_id uuid NOT NULL,
                    claim_version integer NOT NULL,
                    usage_type text NOT NULL,
                    content_hash text NOT NULL,
                    PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type));
                CREATE TABLE recipe_recommendation_knowledge_usage (
                    recommendation_id uuid NOT NULL,
                    claim_id uuid NOT NULL,
                    claim_version integer NOT NULL,
                    usage_type text NOT NULL,
                    content_hash text NOT NULL,
                    PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type));
                CREATE TABLE {retiredArchiveTable} (payload jsonb NOT NULL);
                CREATE TABLE {replayTable} (payload jsonb NOT NULL);
                CREATE TABLE research_evidence (
                    resource_type text NOT NULL,
                    kind text NOT NULL,
                    CONSTRAINT research_evidence_kind_check CHECK (kind IN (
                        'dataset-snapshot', '{retiredTerm}-result', 'analysis-run')));
                CREATE TABLE research_hypothesis_evidence (kind text NOT NULL);
                CREATE TABLE mechanism_claim_evidence (evidence_kind text NOT NULL);
                CREATE TABLE mechanism_claim_lifecycle_decisions (evidence_kind text);
                CREATE TABLE process_research_audit (resource_type text NOT NULL, payload jsonb NOT NULL);
                CREATE TABLE research_recipe_recommendations (payload jsonb NOT NULL);
                CREATE TABLE research_operating_regions (payload jsonb NOT NULL);
                CREATE TABLE research_knowledge_claims (payload jsonb NOT NULL);

                INSERT INTO {oldUsageTable}
                VALUES (
                    '00000000-0000-0000-0000-000000000001',
                    '00000000-0000-0000-0000-000000000002',
                    1, 'supporting', repeat('a', 64));
                INSERT INTO recipe_recommendation_knowledge_usage
                SELECT * FROM {oldUsageTable};
                INSERT INTO {retiredArchiveTable} VALUES (jsonb_build_object());
                INSERT INTO {replayTable}
                VALUES (jsonb_build_object(
                    concat('originalOrder', 'Tri', 'als'), 2,
                    'rawResult', jsonb_build_object(concat('median_', 'tri', 'als'), 2)));
                INSERT INTO research_evidence
                VALUES ('{retiredTerm}-result', '{retiredTerm}-result');
                INSERT INTO research_hypothesis_evidence VALUES ('{retiredTerm}-result');
                INSERT INTO mechanism_claim_evidence VALUES ('{retiredTerm}-result');
                INSERT INTO mechanism_claim_lifecycle_decisions VALUES ('{retiredTerm}-result');
                INSERT INTO process_research_audit
                VALUES (
                    '{retiredTerm}',
                    jsonb_build_object('resourceType', '{retiredTerm}'));
                INSERT INTO research_recipe_recommendations
                VALUES (jsonb_build_object(concat('{retiredTerm}', 'Id'), 'old'));
                INSERT INTO research_operating_regions
                VALUES (jsonb_build_object(
                    concat('supportingExper', 'imentIds'), jsonb_build_array('old'),
                    'supportingResultIds', jsonb_build_array('old'),
                    'evidence', jsonb_build_array(
                        jsonb_build_object('kind', '{retiredTerm}-result'),
                        jsonb_build_object('kind', 'analysis-run'))));
                INSERT INTO research_knowledge_claims
                VALUES (jsonb_build_object(
                    'evidence', jsonb_build_array(
                        jsonb_build_object('kind', '{retiredTerm}-result'),
                        jsonb_build_object('kind', 'analysis-run'))));
                """;
            await new NpgsqlCommand(setupSql, connection).ExecuteNonQueryAsync();

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await new NpgsqlCommand(
                    $"SET LOCAL search_path TO {schema};\n{LoadCleanupMigrationSql()}",
                    connection,
                    transaction).ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }

            var verifySql =
                $"""
                SET search_path TO {schema};
                SELECT
                  to_regclass('{schema}.{oldUsageTable}') IS NULL
                  AND to_regclass('{schema}.{retiredArchiveTable}') IS NULL
                  AND to_regclass('{schema}.{replayTable}') IS NULL
                  AND (SELECT count(*) FROM recipe_recommendation_knowledge_usage) = 1
                  AND NOT EXISTS (SELECT 1 FROM research_evidence
                                  WHERE kind = '{retiredTerm}-result')
                  AND NOT EXISTS (SELECT 1 FROM research_hypothesis_evidence
                                  WHERE kind = '{retiredTerm}-result')
                  AND NOT EXISTS (SELECT 1 FROM mechanism_claim_evidence
                                  WHERE evidence_kind = '{retiredTerm}-result')
                  AND NOT EXISTS (SELECT 1 FROM mechanism_claim_lifecycle_decisions
                                  WHERE evidence_kind = '{retiredTerm}-result')
                  AND NOT EXISTS (SELECT 1 FROM process_research_audit)
                  AND NOT EXISTS (SELECT 1 FROM research_recipe_recommendations
                                  WHERE payload ? concat('{retiredTerm}', 'Id'))
                  AND NOT EXISTS (SELECT 1 FROM research_operating_regions
                                  WHERE payload ? concat('supportingExper', 'imentIds')
                                     OR payload ? 'supportingResultIds'
                                     OR payload::text LIKE '%{retiredTerm}-result%')
                  AND EXISTS (SELECT 1 FROM research_operating_regions
                              WHERE payload::text LIKE '%analysis-run%')
                  AND NOT EXISTS (SELECT 1 FROM research_knowledge_claims
                                  WHERE payload::text LIKE '%{retiredTerm}-result%')
                  AND EXISTS (SELECT 1 FROM research_knowledge_claims
                              WHERE payload::text LIKE '%analysis-run%');
                """;
            Assert.True((bool)(await new NpgsqlCommand(verifySql, connection).ExecuteScalarAsync())!);
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE;", connection)
                .ExecuteNonQueryAsync();
        }
    }

    [LinuxDockerFact]
    public async Task LegacyWorkflowCleanup_ShouldRejectConflictingKnowledgeUsage()
    {
        var schema = $"cleanup_conflict_{Guid.NewGuid():N}";
        var oldUsageTable = "recommendation_" + "knowledge_usage";

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await new NpgsqlCommand(
                $"""
                CREATE SCHEMA {schema};
                SET search_path TO {schema};
                CREATE TABLE {oldUsageTable} (
                    recommendation_id uuid NOT NULL,
                    claim_id uuid NOT NULL,
                    claim_version integer NOT NULL,
                    usage_type text NOT NULL,
                    content_hash text NOT NULL,
                    PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type));
                CREATE TABLE recipe_recommendation_knowledge_usage (
                    recommendation_id uuid NOT NULL,
                    claim_id uuid NOT NULL,
                    claim_version integer NOT NULL,
                    usage_type text NOT NULL,
                    content_hash text NOT NULL,
                    PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type));
                INSERT INTO {oldUsageTable}
                VALUES (
                    '00000000-0000-0000-0000-000000000001',
                    '00000000-0000-0000-0000-000000000002',
                    1, 'supporting', repeat('a', 64));
                INSERT INTO recipe_recommendation_knowledge_usage
                VALUES (
                    '00000000-0000-0000-0000-000000000001',
                    '00000000-0000-0000-0000-000000000002',
                    1, 'supporting', repeat('b', 64));
                """,
                connection).ExecuteNonQueryAsync();

            await using var transaction = await connection.BeginTransactionAsync();
            await Assert.ThrowsAsync<PostgresException>(() =>
                new NpgsqlCommand(
                    $"SET LOCAL search_path TO {schema};\n{LoadCleanupMigrationSql()}",
                    connection,
                    transaction).ExecuteNonQueryAsync());
            await transaction.RollbackAsync();

            var sourceStillExists = await new NpgsqlCommand(
                $"SELECT to_regclass('{schema}.{oldUsageTable}') IS NOT NULL;",
                connection).ExecuteScalarAsync();
            Assert.True((bool)sourceStillExists!);
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE;", connection)
                .ExecuteNonQueryAsync();
        }
    }

    [LinuxDockerFact]
    public async Task RetiredValidationCleanup_ShouldRemoveTransferArtifactsAndEvidence()
    {
        var schema = $"transfer_cleanup_{Guid.NewGuid():N}";
        var transferTable = "research_" + "transfer_assessments";
        var rollbackTable = "research_" + "rollback_drills";
        var retiredKind = "transfer-" + "assessment";

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await new NpgsqlCommand(
                $"""
                CREATE SCHEMA {schema};
                SET search_path TO {schema};
                CREATE TABLE {transferTable} (assessment_id uuid PRIMARY KEY);
                CREATE TABLE {rollbackTable} (drill_id uuid PRIMARY KEY);
                CREATE TABLE research_evidence (
                    resource_type text NOT NULL,
                    kind text NOT NULL,
                    CONSTRAINT research_evidence_kind_check CHECK (kind IN (
                        'dataset-snapshot', '{retiredKind}', 'analysis-run')));
                CREATE TABLE research_hypothesis_evidence (kind text NOT NULL);
                CREATE TABLE mechanism_claim_evidence (evidence_kind text NOT NULL);
                CREATE TABLE mechanism_claim_lifecycle_decisions (evidence_kind text NOT NULL);
                CREATE TABLE process_research_audit (resource_type text NOT NULL, payload jsonb NOT NULL);
                CREATE TABLE research_knowledge_claims (payload jsonb NOT NULL);
                CREATE TABLE research_operating_regions (payload jsonb NOT NULL);

                INSERT INTO research_evidence VALUES
                    ('{retiredKind}', '{retiredKind}'),
                    ('analysis-run', 'analysis-run');
                INSERT INTO research_hypothesis_evidence VALUES ('{retiredKind}'), ('analysis-run');
                INSERT INTO mechanism_claim_evidence VALUES ('{retiredKind}'), ('analysis-run');
                INSERT INTO mechanism_claim_lifecycle_decisions VALUES ('{retiredKind}'), ('analysis-run');
                INSERT INTO process_research_audit VALUES
                    ('{retiredKind}', jsonb_build_object('resourceType', '{retiredKind}')),
                    ('analysis-run', jsonb_build_object('resourceType', 'analysis-run'));
                INSERT INTO research_knowledge_claims VALUES (jsonb_build_object(
                    'transferAssessmentId', 'retired',
                    'evidence', jsonb_build_array(
                        jsonb_build_object('kind', '{retiredKind}'),
                        jsonb_build_object('kind', 'analysis-run'))));
                INSERT INTO research_operating_regions VALUES (jsonb_build_object(
                    'name', 'mixed',
                    'evidence', jsonb_build_array(
                        jsonb_build_object('kind', '{retiredKind}'),
                        jsonb_build_object('Kind', '{retiredKind}'),
                        jsonb_build_object('kind', 'analysis-run')))),
                    (jsonb_build_object('name', 'retired-only', 'evidence', jsonb_build_array(
                        jsonb_build_object('kind', '{retiredKind}')))),
                    (jsonb_build_object('name', 'no-evidence'));
                """,
                connection).ExecuteNonQueryAsync();

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await new NpgsqlCommand(
                    $"SET LOCAL search_path TO {schema};\n{LoadMigrationSql("0023_remove_retired_validation_artifacts")}",
                    connection,
                    transaction).ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }

            var clean = await new NpgsqlCommand(
                $"""
                SET search_path TO {schema};
                SELECT to_regclass('{schema}.{transferTable}') IS NULL
                   AND to_regclass('{schema}.{rollbackTable}') IS NULL
                   AND NOT EXISTS (SELECT 1 FROM research_evidence WHERE kind = '{retiredKind}')
                   AND (SELECT count(*) FROM research_evidence WHERE kind = 'analysis-run') = 1
                   AND NOT EXISTS (SELECT 1 FROM research_hypothesis_evidence WHERE kind = '{retiredKind}')
                   AND NOT EXISTS (SELECT 1 FROM mechanism_claim_evidence WHERE evidence_kind = '{retiredKind}')
                   AND NOT EXISTS (SELECT 1 FROM mechanism_claim_lifecycle_decisions WHERE evidence_kind = '{retiredKind}')
                   AND NOT EXISTS (SELECT 1 FROM process_research_audit WHERE resource_type = '{retiredKind}')
                   AND (SELECT count(*) FROM process_research_audit WHERE resource_type = 'analysis-run') = 1
                   AND NOT EXISTS (SELECT 1 FROM research_knowledge_claims
                                   WHERE payload ? 'transferAssessmentId'
                                      OR payload::text LIKE '%{retiredKind}%')
                   AND NOT EXISTS (SELECT 1 FROM research_operating_regions
                                   WHERE payload::text LIKE '%{retiredKind}%')
                   AND EXISTS (SELECT 1 FROM research_knowledge_claims
                               WHERE payload::text LIKE '%analysis-run%')
                   AND EXISTS (SELECT 1 FROM research_operating_regions
                               WHERE payload->>'name' = 'mixed'
                                 AND payload->'evidence' = jsonb_build_array(jsonb_build_object('kind', 'analysis-run')))
                   AND EXISTS (SELECT 1 FROM research_operating_regions
                               WHERE payload->>'name' = 'retired-only'
                                 AND payload->'evidence' = '[]'::jsonb)
                   AND EXISTS (SELECT 1 FROM research_operating_regions
                               WHERE payload = jsonb_build_object('name', 'no-evidence'));
                """,
                connection).ExecuteScalarAsync();
            Assert.True((bool)clean!);

            var rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
                await new NpgsqlCommand(
                    $"INSERT INTO research_evidence VALUES ('analysis-run', '{retiredKind}');",
                    connection).ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE;", connection)
                .ExecuteNonQueryAsync();
        }
    }

    [LinuxDockerFact]
    public async Task TimeSeriesSamples_ShouldUseSingleSourceSchema()
    {
        var runner = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        await runner.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              to_regclass('public.time_series_samples') IS NULL
              AND to_regclass('public.process_sample_frames') IS NOT NULL
              AND to_regclass('public.process_sample_values') IS NOT NULL
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_frames'
                  AND column_name = 'ingested_at')
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'collection_points'
                  AND column_name = 'point_key' AND data_type = 'bigint')
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_values'
                  AND column_name = 'quality_code' AND data_type = 'smallint')
              AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_values'
                  AND column_name IN (
                    'event_id', 'execution_id', 'edge_id', 'source', 'subject_type',
                    'subject_id', 'data_model_id', 'data_model_version', 'signal_code',
                    'collection_point_id'))
              AND to_regclass('public.ix_time_series_samples_context') IS NULL
              AND to_regclass('public.production_event_stream') IS NULL
              AND to_regclass('public.projected_process_sample_events') IS NULL;
            """,
            connection);
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task ProductionCellIdentity_ShouldBeRequiredAcrossCanonicalStores()
    {
        await postgres.EnsureSchemaAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) = 6
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN (
                'collection_points', 'data_object_operation_keys', 'data_object_summaries',
                'event_ingest_keys', 'process_sample_frames', 'production_events')
              AND column_name = 'site_id'
              AND is_nullable = 'NO';
            """,
            connection);

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task ProductionEnvelopeIntegrity_ShouldBeRequiredBySchema()
    {
        await postgres.EnsureSchemaAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) = 1
               FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'event_ingest_keys'
                 AND column_name = 'payload_hash' AND is_nullable = 'NO')
              AND
              (SELECT count(*) = 7
               FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'production_events'
                 AND column_name IN (
                   'schema_version', 'configuration_kind', 'configuration_id',
                   'configuration_version', 'quality_flags', 'payload_hash', 'site_id'));
            """,
            connection);

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task ResearchEvidenceForeignKeys_ShouldBeValidatedAndNonCascading()
    {
        await postgres.EnsureSchemaAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) = 4
                   AND bool_and(convalidated)
                   AND bool_and(confdeltype = 'a')
            FROM pg_constraint
            WHERE conname IN (
              'rr_recommendations_project_fk',
              'rr_decisions_project_recommendation_fk',
              'rr_decision_executions_project_decision_fk',
               'rr_decision_outcomes_project_decision_fk');
            """,
            connection);

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task TimeSeriesRetention_ShouldPruneFramesAndValuesTogether()
    {
        await postgres.EnsureSchemaAsync();
        using var store = new PostgresTimeSeriesStore(
            postgres.DataSource,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            Options.Create(new PlatformEventOptions()));
        await store.InitializeAsync();
        var frameId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-400);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO process_sample_frames (
                           occurred_at, frame_id, event_id, recorded_at, ingested_at,
                           site_id, edge_id, source, subject_type, subject_id, data_model_id, data_model_version)
                         VALUES (
                           @at, @frame_id, @event_id, @at, @at,
                           'SITE-RETENTION', 'EDGE-RETENTION', 'test', 'equipment', 'PRESS-RETENTION', 'retention-model', 1);
                         INSERT INTO process_sample_values (
                           occurred_at, frame_id, point_key, quality_code, numeric_value)
                         VALUES (@at, @frame_id, 1, 0, 1.0);
                         """,
                         connection))
        {
            insert.Parameters.AddWithValue("at", occurredAt.UtcDateTime);
            insert.Parameters.AddWithValue("frame_id", frameId);
            insert.Parameters.AddWithValue("event_id", $"retention-{frameId}");
            await insert.ExecuteNonQueryAsync();
        }

        await TimeSeriesRetentionHostedService.PruneAsync(postgres.DataSource, 90);

        await using var count = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM process_sample_frames WHERE frame_id = @frame_id),
              (SELECT count(*) FROM process_sample_values WHERE frame_id = @frame_id);
            """,
            connection);
        count.Parameters.AddWithValue("frame_id", frameId);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
    }

    private static string LoadCleanupMigrationSql()
        => LoadMigrationSql("0022_remove_legacy_workflow_artifacts");

    private static string LoadMigrationSql(string name)
    {
        var resource = $"Ingot.Platform.Infrastructure.Migrations.sql.{name}.sql";
        using var stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded migration {resource} was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
