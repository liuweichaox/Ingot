using Ingot.Platform.Application.ProcessExecutions;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
/// PostgreSQL 实现的运行边界存储。
/// </summary>
public sealed class PostgresExecutionBoundaryStore : IExecutionBoundaryStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresExecutionBoundaryStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<ExecutionBoundary?> GetBoundaryAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                execution_id, site_id, edge_id, source_execution_id,
                started_at, ended_at, status, event_count, min_ingest_id, max_ingest_id,
                confidence, confidence_reason, last_observed_at, created_at, updated_at, gap_detected
            FROM process_execution_boundaries
            WHERE site_id = $1 AND source_execution_id = $2
            ORDER BY created_at DESC
            LIMIT 1
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(siteId);
        command.Parameters.AddWithValue(sourceExecutionId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadBoundary(reader);
    }

    public async Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        await UpsertProjectedBoundaryAsync(boundary, ct).ConfigureAwait(false);
    }

    public async Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct)
    {
        const string sql = """
            UPDATE process_execution_boundaries
            SET ended_at = $1, status = $2, event_count = $3, max_ingest_id = $4,
                confidence = $5, confidence_reason = $6, last_observed_at = $7, updated_at = $8,
                gap_detected = gap_detected OR $9
            WHERE execution_id = $10
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(boundary.EndedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue((int)boundary.Status);
        command.Parameters.AddWithValue(boundary.EventCount);
        command.Parameters.AddWithValue(boundary.MaxIngestId);
        command.Parameters.AddWithValue((int)boundary.Confidence);
        command.Parameters.AddWithValue(boundary.ConfidenceReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(boundary.LastObservedAt);
        command.Parameters.AddWithValue(boundary.UpdatedAt);
        command.Parameters.AddWithValue(boundary.GapDetected);
        command.Parameters.AddWithValue(boundary.ExecutionId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
        string siteId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var sql = """
            SELECT
                execution_id, site_id, edge_id, source_execution_id,
                started_at, ended_at, status, event_count, min_ingest_id, max_ingest_id,
                confidence, confidence_reason, last_observed_at, created_at, updated_at, gap_detected
            FROM process_execution_boundaries
            WHERE site_id = $1
            """;

        var paramIndex = 2;
        if (from.HasValue)
        {
            sql += $" AND started_at >= ${paramIndex++}";
        }
        if (to.HasValue)
        {
            sql += $" AND started_at <= ${paramIndex++}";
        }

        sql += " ORDER BY started_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(siteId);

        paramIndex = 2;
        if (from.HasValue)
        {
            command.Parameters.AddWithValue(from.Value);
            paramIndex++;
        }
        if (to.HasValue)
        {
            command.Parameters.AddWithValue(to.Value);
            paramIndex++;
        }

        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);

        var boundaries = new List<ExecutionBoundary>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            boundaries.Add(ReadBoundary(reader));
        }

        return boundaries;
    }

    public async Task<ExecutionBoundaryProjectionLease?> ClaimProjectionAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct)
    {
        var leaseId = Guid.CreateVersion7();
        await using var command = _dataSource.CreateCommand(
            """
            WITH candidate AS (
              SELECT site_id, source_execution_id
              FROM execution_boundary_recompute_jobs
              WHERE (status = 'queued' AND available_at <= now())
                 OR (status = 'running' AND leased_at < now() - @lease_timeout)
              ORDER BY available_at, updated_at
              FOR UPDATE SKIP LOCKED
              LIMIT 1
            )
            UPDATE execution_boundary_recompute_jobs AS job
            SET status = 'running', lease_id = @lease_id, leased_at = now(),
                attempt_count = attempt_count + 1, updated_at = now()
            FROM candidate
            WHERE job.site_id = candidate.site_id
              AND job.source_execution_id = candidate.source_execution_id
            RETURNING job.site_id, job.edge_id, job.source_execution_id,
                      job.requested_max_ingest_id, job.gap_detected, job.attempt_count;
            """);
        command.Parameters.AddWithValue("lease_timeout", leaseTimeout);
        command.Parameters.AddWithValue("lease_id", leaseId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new ExecutionBoundaryProjectionLease(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetBoolean(4),
                reader.GetInt32(5),
                leaseId)
            : null;
    }

    public async Task<ExecutionBoundaryProjectionResult?> ProjectAsync(
        ExecutionBoundaryProjectionLease lease,
        TimeSpan executionTimeout,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            WITH records AS (
              SELECT ingest_id, edge_id, event_type, occurred_at, recorded_at
              FROM production_events
              WHERE site_id = @site_id AND execution_id = @execution_id
              UNION ALL
              SELECT frame_id AS ingest_id, edge_id, 'process.sample'::text AS event_type,
                     occurred_at, recorded_at
              FROM process_sample_frames
              WHERE site_id = @site_id AND execution_id = @execution_id
            )
            SELECT count(*)::bigint, min(ingest_id), max(ingest_id),
                   min(occurred_at), max(occurred_at), max(recorded_at),
                   min(occurred_at) FILTER (WHERE event_type = 'process.execution.started'),
                   max(occurred_at) FILTER (
                     WHERE event_type = 'process.execution.completed'),
                   (array_agg(edge_id ORDER BY ingest_id DESC))[1]
            FROM records;
            """);
        command.Parameters.AddWithValue("site_id", lease.SiteId);
        command.Parameters.AddWithValue("execution_id", lease.SourceExecutionId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetInt64(0) == 0)
            return null;

        var count = checked((int)reader.GetInt64(0));
        var minIngestId = reader.GetInt64(1);
        var maxIngestId = reader.GetInt64(2);
        var firstOccurredAt = reader.GetFieldValue<DateTimeOffset>(3);
        var lastOccurredAt = reader.GetFieldValue<DateTimeOffset>(4);
        var lastObservedAt = reader.GetFieldValue<DateTimeOffset>(5);
        DateTimeOffset? explicitStartedAt = reader.IsDBNull(6)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(6);
        DateTimeOffset? explicitEndedAt = reader.IsDBNull(7)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(7);
        var edgeId = reader.IsDBNull(8) ? lease.EdgeId : reader.GetString(8);
        await reader.DisposeAsync().ConfigureAwait(false);

        var existing = await GetBoundaryAsync(lease.SiteId, lease.SourceExecutionId, ct).ConfigureAwait(false);
        var gapDetected = lease.GapDetected || existing?.GapDetected == true;
        var now = DateTimeOffset.UtcNow;
        var timeoutAt = lastOccurredAt + executionTimeout;
        var inferredEnd = !explicitEndedAt.HasValue && now >= timeoutAt;
        var endedAt = explicitEndedAt ?? (inferredEnd ? timeoutAt : null);
        var reasons = new List<string>();
        if (!explicitStartedAt.HasValue)
            reasons.Add("无 process.execution.started 事件，用第一条事件时间推断。");
        if (!explicitEndedAt.HasValue)
        {
            reasons.Add(inferredEnd
                ? $"无 process.execution.completed 事件，用超时（{executionTimeout.TotalHours} 小时）推断结束。"
                : "无 process.execution.completed 事件，运行状态为 InProgress。");
        }
        if (gapDetected)
            reasons.Add("所属 Edge 事件序列检测到缺口。");

        var confidence = gapDetected
            ? ExecutionBoundaryConfidence.Fragmented
            : explicitStartedAt.HasValue && explicitEndedAt.HasValue
                ? ExecutionBoundaryConfidence.Complete
                : inferredEnd
                    ? ExecutionBoundaryConfidence.InferredEnd
                    : ExecutionBoundaryConfidence.Fragmented;
        var boundary = new ExecutionBoundary
        {
            ExecutionId = existing?.ExecutionId ?? Guid.CreateVersion7().ToString(),
            SiteId = lease.SiteId,
            EdgeId = edgeId,
            SourceExecutionId = lease.SourceExecutionId,
            StartedAt = explicitStartedAt ?? firstOccurredAt,
            EndedAt = endedAt,
            Status = endedAt.HasValue ? ExecutionBoundaryStatus.Completed : ExecutionBoundaryStatus.InProgress,
            EventCount = count,
            MinIngestId = minIngestId,
            MaxIngestId = maxIngestId,
            Confidence = confidence,
            ConfidenceReason = reasons.Count == 0 ? null : string.Join(' ', reasons),
            GapDetected = gapDetected,
            LastObservedAt = lastObservedAt,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        await UpsertProjectedBoundaryAsync(boundary, ct).ConfigureAwait(false);
        return new ExecutionBoundaryProjectionResult(
            boundary,
            endedAt.HasValue ? null : timeoutAt);
    }

    public async Task<bool> FinishProjectionAsync(
        ExecutionBoundaryProjectionLease lease,
        DateTimeOffset? recheckAt,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var delete = new NpgsqlCommand(
            """
            DELETE FROM execution_boundary_recompute_jobs
            WHERE site_id = @site_id AND source_execution_id = @execution_id
              AND status = 'running' AND lease_id = @lease_id
              AND requested_max_ingest_id <= @processed_max_ingest_id
              AND NOT @has_recheck;
            """,
            connection,
            transaction);
        AddLeaseParameters(delete, lease);
        delete.Parameters.AddWithValue("has_recheck", recheckAt.HasValue);
        if (await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }

        await using var release = new NpgsqlCommand(
            """
            UPDATE execution_boundary_recompute_jobs
            SET status = 'queued', lease_id = NULL, leased_at = NULL,
                available_at = CASE
                  WHEN requested_max_ingest_id > @processed_max_ingest_id THEN now()
                  ELSE @recheck_at
                END,
                last_error = NULL, updated_at = now()
            WHERE site_id = @site_id AND source_execution_id = @execution_id
              AND status = 'running' AND lease_id = @lease_id;
            """,
            connection,
            transaction);
        AddLeaseParameters(release, lease);
        release.Parameters.AddWithValue("recheck_at", recheckAt ?? DateTimeOffset.UtcNow);
        var released = await release.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return released;
    }

    public async Task<bool> RetryProjectionAsync(
        ExecutionBoundaryProjectionLease lease,
        string error,
        TimeSpan delay,
        int maxAttempts,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE execution_boundary_recompute_jobs
            SET status = CASE WHEN attempt_count >= @max_attempts THEN 'failed' ELSE 'queued' END,
                lease_id = NULL, leased_at = NULL,
                available_at = now() + @delay, last_error = @error,
                failed_at = CASE WHEN attempt_count >= @max_attempts THEN now() ELSE NULL END,
                updated_at = now()
            WHERE site_id = @site_id AND source_execution_id = @execution_id
              AND status = 'running' AND lease_id = @lease_id;
            """);
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("delay", delay);
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("error", error.Length <= 2000 ? error : error[..2000]);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private async Task UpsertProjectedBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_execution_boundaries(
              execution_id, site_id, edge_id, source_execution_id, started_at, ended_at,
              status, event_count, min_ingest_id, max_ingest_id, confidence, confidence_reason,
              last_observed_at, created_at, updated_at, gap_detected)
            VALUES (
              @execution_id, @site_id, @edge_id, @source_execution_id, @started_at, @ended_at,
              @status, @event_count, @min_ingest_id, @max_ingest_id, @confidence, @confidence_reason,
              @last_observed_at, @created_at, @updated_at, @gap_detected)
            ON CONFLICT (site_id, source_execution_id) WHERE status != 2 DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              started_at = EXCLUDED.started_at,
              ended_at = EXCLUDED.ended_at,
              status = EXCLUDED.status,
              event_count = EXCLUDED.event_count,
              min_ingest_id = EXCLUDED.min_ingest_id,
              max_ingest_id = EXCLUDED.max_ingest_id,
              confidence = EXCLUDED.confidence,
              confidence_reason = EXCLUDED.confidence_reason,
              last_observed_at = EXCLUDED.last_observed_at,
              updated_at = EXCLUDED.updated_at,
              gap_detected = process_execution_boundaries.gap_detected OR EXCLUDED.gap_detected;
            """);
        command.Parameters.AddWithValue("execution_id", boundary.ExecutionId);
        command.Parameters.AddWithValue("site_id", boundary.SiteId);
        command.Parameters.AddWithValue("edge_id", boundary.EdgeId);
        command.Parameters.AddWithValue("source_execution_id", boundary.SourceExecutionId);
        command.Parameters.AddWithValue("started_at", boundary.StartedAt);
        command.Parameters.AddWithValue("ended_at", boundary.EndedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("status", (int)boundary.Status);
        command.Parameters.AddWithValue("event_count", boundary.EventCount);
        command.Parameters.AddWithValue("min_ingest_id", boundary.MinIngestId);
        command.Parameters.AddWithValue("max_ingest_id", boundary.MaxIngestId);
        command.Parameters.AddWithValue("confidence", (int)boundary.Confidence);
        command.Parameters.AddWithValue("confidence_reason", boundary.ConfidenceReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("last_observed_at", boundary.LastObservedAt);
        command.Parameters.AddWithValue("created_at", boundary.CreatedAt);
        command.Parameters.AddWithValue("updated_at", boundary.UpdatedAt);
        command.Parameters.AddWithValue("gap_detected", boundary.GapDetected);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddLeaseParameters(NpgsqlCommand command, ExecutionBoundaryProjectionLease lease)
    {
        command.Parameters.AddWithValue("site_id", lease.SiteId);
        command.Parameters.AddWithValue("execution_id", lease.SourceExecutionId);
        command.Parameters.AddWithValue("lease_id", lease.LeaseId);
        command.Parameters.AddWithValue("processed_max_ingest_id", lease.RequestedMaxIngestId);
    }

    private static ExecutionBoundary ReadBoundary(NpgsqlDataReader reader)
    {
        return new ExecutionBoundary
        {
            ExecutionId = reader.GetString(0),
            SiteId = reader.GetString(1),
            EdgeId = reader.GetString(2),
            SourceExecutionId = reader.GetString(3),
            StartedAt = reader.GetFieldValue<DateTimeOffset>(4),
            EndedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            Status = (ExecutionBoundaryStatus)reader.GetInt32(6),
            EventCount = reader.GetInt32(7),
            MinIngestId = reader.GetInt64(8),
            MaxIngestId = reader.GetInt64(9),
            Confidence = (ExecutionBoundaryConfidence)reader.GetInt32(10),
            ConfidenceReason = reader.IsDBNull(11) ? null : reader.GetString(11),
            LastObservedAt = reader.GetFieldValue<DateTimeOffset>(12),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(13),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(14),
            GapDetected = reader.GetBoolean(15)
        };
    }
}

public sealed record ExecutionBoundaryProjectionLease(
    string SiteId,
    string EdgeId,
    string SourceExecutionId,
    long RequestedMaxIngestId,
    bool GapDetected,
    int AttemptCount,
    Guid LeaseId);

public sealed record ExecutionBoundaryProjectionResult(
    ExecutionBoundary Boundary,
    DateTimeOffset? RecheckAt);
