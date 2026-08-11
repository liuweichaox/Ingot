using System.Text.Json;
using Ingot.Contracts.Events;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class PostgresProcessExecutionAnalysisMaterializationStore : IProcessExecutionAnalysisMaterializationStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessExecutionAnalysisMaterializationStore(
        IConfiguration configuration,
        ILogger<PostgresProcessExecutionAnalysisMaterializationStore> logger)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _ = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<ProcessExecutionAnalysisSnapshot?> TryLoadAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT result::text, computed_at
            FROM execution_analysis_materializations
            WHERE execution_id = @execution_id
              AND algorithm_version = @algorithm_version
              AND data_model_id = @data_model_id
              AND data_model_version = @data_model_version
              AND analysis_plan_id = @analysis_plan_id
              AND analysis_plan_version = @analysis_plan_version
              AND source_min_ingest_id = @source_min_ingest_id
              AND source_max_ingest_id = @source_max_ingest_id
              AND source_event_count = @source_event_count
              AND source_content_hash = @source_content_hash
              AND status = 'ready';
            """);
        AddKeyParameters(command, key);
        AddSourceParameters(command, source);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var result = JsonSerializer.Deserialize<WholeProcessExecutionAnalysisResult>(reader.GetString(0), JsonOptions)
                     ?? throw new InvalidOperationException($"过程执行 {key.ExecutionId} 的物化分析结果无法反序列化。");
        return new ProcessExecutionAnalysisSnapshot(
            result,
            reader.GetFieldValue<DateTimeOffset>(1),
            source);
    }

    public async Task<ProcessExecutionAnalysisSnapshot> SaveAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var computedAt = DateTimeOffset.UtcNow;
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await UpsertMaterializationAsync(
            connection, transaction, key, source, computedAt, analysis, ct)
            .ConfigureAwait(false);
        await DeleteDetailsAsync(connection, transaction, key, ct).ConfigureAwait(false);
        await InsertPhasesAsync(connection, transaction, key, analysis, ct).ConfigureAwait(false);
        await InsertFeaturesAsync(connection, transaction, key, analysis, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new ProcessExecutionAnalysisSnapshot(analysis, computedAt, source);
    }

    public async Task MarkDirtyAsync(
        IReadOnlyCollection<string> executionIds,
        long invalidatedSourceMaxIngestId,
        string reason,
        CancellationToken ct = default)
    {
        var ids = executionIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return;

        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE execution_analysis_materializations
            SET status = 'dirty',
                invalidated_at = now(),
                invalidated_source_max_ingest_id =
                  GREATEST(invalidated_source_max_ingest_id, @invalidated_source_max_ingest_id),
                invalidation_reason = @reason
            WHERE execution_id = ANY(@execution_ids);
            """);
        command.Parameters.AddWithValue("invalidated_source_max_ingest_id", invalidatedSourceMaxIngestId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("execution_ids", ids);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO execution_analysis_backfill_jobs(job_id, status, payload, created_at, updated_at)
            VALUES (@job_id, @status, @payload, @created_at, now());
            """);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("status", job.Status);
        command.Parameters.AddWithValue(
            "payload",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(job, JsonOptions));
        command.Parameters.AddWithValue("created_at", job.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return job;
    }

    public async Task<ProcessExecutionAnalysisBackfillJob> SaveBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE execution_analysis_backfill_jobs
            SET status = @status, payload = @payload, updated_at = now()
            WHERE job_id = @job_id;
            """);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("status", job.Status);
        command.Parameters.AddWithValue(
            "payload",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(job, JsonOptions));
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException("过程执行分析回填任务不存在。");
        return job;
    }

    public async Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM execution_analysis_backfill_jobs WHERE job_id = @job_id;");
        command.Parameters.AddWithValue("job_id", jobId);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null
            ? null
            : JsonSerializer.Deserialize<ProcessExecutionAnalysisBackfillJob>(payload, JsonOptions);
    }

    public async Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM execution_analysis_backfill_jobs ORDER BY created_at DESC LIMIT 100;");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var jobs = new List<ProcessExecutionAnalysisBackfillJob>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var job = JsonSerializer.Deserialize<ProcessExecutionAnalysisBackfillJob>(reader.GetString(0), JsonOptions);
            if (job is not null)
                jobs.Add(job);
        }
        return jobs;
    }

    public async Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT
              f.signal_code,
              f.phase_code,
              f.feature_code,
              COUNT(DISTINCT f.execution_id),
              MIN(f.feature_value),
              MAX(f.feature_value),
              AVG(f.feature_value),
              STDDEV_SAMP(f.feature_value),
              percentile_cont(0.10) WITHIN GROUP (ORDER BY f.feature_value),
              percentile_cont(0.50) WITHIN GROUP (ORDER BY f.feature_value),
              percentile_cont(0.90) WITHIN GROUP (ORDER BY f.feature_value)
            FROM execution_features f
            JOIN execution_analysis_materializations m
              ON m.execution_id = f.execution_id
             AND m.algorithm_version = f.algorithm_version
             AND m.data_model_id = f.data_model_id
             AND m.data_model_version = f.data_model_version
             AND m.analysis_plan_id = f.analysis_plan_id
             AND m.analysis_plan_version = f.analysis_plan_version
            WHERE m.status = 'ready'
              AND f.feature_value IS NOT NULL
              AND (@signal_code IS NULL OR f.signal_code = @signal_code)
              AND (@phase_code IS NULL OR f.phase_code = @phase_code)
              AND (@feature_code IS NULL OR f.feature_code = @feature_code)
              AND (@from IS NULL OR f.started_at >= @from)
              AND (@to IS NULL OR f.started_at <= @to)
            GROUP BY f.signal_code, f.phase_code, f.feature_code
            ORDER BY COUNT(DISTINCT f.execution_id) DESC,
                     f.signal_code, f.phase_code, f.feature_code
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue(
            "signal_code",
            NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(signalCode) ? DBNull.Value : signalCode.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue(
            "phase_code",
            NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(phaseCode) ? DBNull.Value : phaseCode.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue(
            "feature_code",
            NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(featureCode) ? DBNull.Value : featureCode.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue(
            "from",
            NpgsqlDbType.TimestampTz,
            from is null ? DBNull.Value : from.Value);
        command.Parameters.AddWithValue(
            "to",
            NpgsqlDbType.TimestampTz,
            to is null ? DBNull.Value : to.Value);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<ProcessExecutionFeatureAggregate>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ProcessExecutionFeatureAggregate
            {
                SignalCode = reader.GetString(0),
                PhaseCode = reader.GetString(1),
                FeatureCode = reader.GetString(2),
                ProcessExecutionCount = reader.GetInt64(3),
                Minimum = reader.GetDouble(4),
                Maximum = reader.GetDouble(5),
                Average = reader.GetDouble(6),
                StandardDeviation = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                P10 = reader.GetDouble(8),
                Median = reader.GetDouble(9),
                P90 = reader.GetDouble(10)
            });
        }
        return rows;
    }

    public async Task<IReadOnlyList<string>> ListDirtyExecutionIdsAsync(
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT execution_id
            FROM execution_analysis_materializations
            WHERE status = 'dirty'
            GROUP BY execution_id
            ORDER BY MIN(invalidated_at)
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var ids = new List<string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task UpsertMaterializationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        DateTimeOffset computedAt,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO execution_analysis_materializations (
              execution_id, algorithm_version,
              data_model_id, data_model_version,
              analysis_plan_id, analysis_plan_version,
              source_min_ingest_id, source_max_ingest_id, source_event_count, source_content_hash,
              status, computed_at, result)
            VALUES (
              @execution_id, @algorithm_version,
              @data_model_id, @data_model_version,
              @analysis_plan_id, @analysis_plan_version,
              @source_min_ingest_id, @source_max_ingest_id, @source_event_count, @source_content_hash,
              'ready', @computed_at, @result)
            ON CONFLICT (
              execution_id, algorithm_version,
              data_model_id, data_model_version,
              analysis_plan_id, analysis_plan_version)
            DO UPDATE SET
              source_min_ingest_id = EXCLUDED.source_min_ingest_id,
              source_max_ingest_id = EXCLUDED.source_max_ingest_id,
              source_event_count = EXCLUDED.source_event_count,
              source_content_hash = EXCLUDED.source_content_hash,
              status = CASE
                WHEN execution_analysis_materializations.invalidated_source_max_ingest_id >
                     EXCLUDED.source_max_ingest_id
                  THEN 'dirty'
                ELSE 'ready'
              END,
              computed_at = EXCLUDED.computed_at,
              invalidated_at = CASE
                WHEN execution_analysis_materializations.invalidated_source_max_ingest_id >
                     EXCLUDED.source_max_ingest_id
                  THEN execution_analysis_materializations.invalidated_at
                ELSE NULL
              END,
              invalidation_reason = CASE
                WHEN execution_analysis_materializations.invalidated_source_max_ingest_id >
                     EXCLUDED.source_max_ingest_id
                  THEN execution_analysis_materializations.invalidation_reason
                ELSE NULL
              END,
              result = EXCLUDED.result;
            """,
            connection,
            transaction);
        AddKeyParameters(command, key);
        AddSourceParameters(command, source);
        command.Parameters.AddWithValue("computed_at", computedAt);
        command.Parameters.AddWithValue(
            "result",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(analysis, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task DeleteDetailsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProcessExecutionAnalysisMaterializationKey key,
        CancellationToken ct)
    {
        foreach (var table in new[] { "execution_features", "execution_phases" })
        {
            await using var command = new NpgsqlCommand(
                $"""
                 DELETE FROM {table}
                 WHERE execution_id = @execution_id
                   AND algorithm_version = @algorithm_version
                   AND data_model_id = @data_model_id
                   AND data_model_version = @data_model_version
                   AND analysis_plan_id = @analysis_plan_id
                   AND analysis_plan_version = @analysis_plan_version;
                 """,
                connection,
                transaction);
            AddKeyParameters(command, key);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertPhasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProcessExecutionAnalysisMaterializationKey key,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct)
    {
        if (analysis.Phases.Count == 0)
            return;
        var rows = analysis.Phases.Select(phase => new Dictionary<string, object?>
        {
            ["phase_code"] = phase.Code,
            ["phase_name"] = phase.Name,
            ["phase_order"] = phase.Order,
            ["phase_source"] = phase.Source,
            ["sample_count"] = phase.SampleCount,
            ["started_at"] = phase.StartedAt,
            ["ended_at"] = phase.EndedAt
        }).ToArray();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO execution_phases (
              execution_id, algorithm_version,
              data_model_id, data_model_version,
              analysis_plan_id, analysis_plan_version,
              phase_code, phase_name, phase_order, phase_source,
              required, is_complete, sample_count, started_at, ended_at)
            SELECT
              @execution_id, @algorithm_version,
              @data_model_id, @data_model_version,
              @analysis_plan_id, @analysis_plan_version,
              row.phase_code, row.phase_name, row.phase_order, row.phase_source,
              FALSE, TRUE, row.sample_count, row.started_at, row.ended_at
            FROM jsonb_to_recordset(@rows) AS row(
              phase_code TEXT,
              phase_name TEXT,
              phase_order INTEGER,
              phase_source TEXT,
              sample_count INTEGER,
              started_at TIMESTAMPTZ,
              ended_at TIMESTAMPTZ);
            """,
            connection,
            transaction);
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("rows", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rows, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertFeaturesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProcessExecutionAnalysisMaterializationKey key,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct)
    {
        var rows = analysis.Signals.SelectMany(signal => signal.Features.Select(feature =>
            new Dictionary<string, object?>
            {
                ["signal_code"] = signal.Code,
                ["signal_name"] = signal.Name,
                ["signal_unit"] = signal.Unit,
                ["signal_sample_count"] = signal.SampleCount,
                ["phase_code"] = feature.PhaseCode ?? string.Empty,
                ["phase_name"] = feature.PhaseName,
                ["phase_order"] = feature.PhaseOrder ?? 0,
                ["phase_source"] = feature.PhaseSource,
                ["feature_code"] = feature.Code,
                ["feature_definition_version"] = feature.DefinitionVersion,
                ["feature_definition_hash"] = feature.DefinitionHash,
                ["computation_hash"] = feature.ComputationHash,
                ["input_point_count"] = feature.InputPointCount,
                ["feature_value"] = feature.Value,
                ["valid_duration_ms"] = feature.ValidDurationMs,
                ["coverage"] = feature.Coverage,
                ["started_at"] = feature.StartedAt,
                ["ended_at"] = feature.EndedAt
            })).ToArray();
        if (rows.Length == 0)
            return;
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO execution_features (
              execution_id, algorithm_version,
              data_model_id, data_model_version,
              analysis_plan_id, analysis_plan_version,
              signal_code, signal_name, signal_unit, signal_sample_count,
              phase_code, phase_name, phase_order, phase_source,
              feature_code, feature_definition_version, feature_definition_hash,
              computation_hash, input_point_count,
              feature_value, valid_duration_ms, coverage,
              started_at, ended_at)
            SELECT
              @execution_id, @algorithm_version,
              @data_model_id, @data_model_version,
              @analysis_plan_id, @analysis_plan_version,
              row.signal_code, row.signal_name, row.signal_unit, row.signal_sample_count,
              row.phase_code, row.phase_name, row.phase_order, row.phase_source,
              row.feature_code, row.feature_definition_version, row.feature_definition_hash,
              row.computation_hash, row.input_point_count,
              row.feature_value, row.valid_duration_ms, row.coverage,
              row.started_at, row.ended_at
            FROM jsonb_to_recordset(@rows) AS row(
              signal_code TEXT,
              signal_name TEXT,
              signal_unit TEXT,
              signal_sample_count INTEGER,
              phase_code TEXT,
              phase_name TEXT,
              phase_order INTEGER,
              phase_source TEXT,
              feature_code TEXT,
              feature_definition_version INTEGER,
              feature_definition_hash TEXT,
              computation_hash TEXT,
              input_point_count INTEGER,
              feature_value DOUBLE PRECISION,
              valid_duration_ms DOUBLE PRECISION,
              coverage DOUBLE PRECISION,
              started_at TIMESTAMPTZ,
              ended_at TIMESTAMPTZ);
            """,
            connection,
            transaction);
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("rows", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rows, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddKeyParameters(NpgsqlCommand command, ProcessExecutionAnalysisMaterializationKey key)
    {
        command.Parameters.AddWithValue("execution_id", key.ExecutionId);
        command.Parameters.AddWithValue("algorithm_version", key.AlgorithmVersion);
        command.Parameters.AddWithValue("data_model_id", key.DataModelId);
        command.Parameters.AddWithValue("data_model_version", key.DataModelVersion);
        command.Parameters.AddWithValue("analysis_plan_id", key.AnalysisPlanId);
        command.Parameters.AddWithValue("analysis_plan_version", key.AnalysisPlanVersion);
    }

    private static void AddSourceParameters(NpgsqlCommand command, ProcessExecutionAnalysisSourceFingerprint source)
    {
        command.Parameters.AddWithValue("source_min_ingest_id", source.MinIngestId);
        command.Parameters.AddWithValue("source_max_ingest_id", source.MaxIngestId);
        command.Parameters.AddWithValue("source_event_count", source.EventCount);
        command.Parameters.AddWithValue("source_content_hash", source.ContentHash);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
