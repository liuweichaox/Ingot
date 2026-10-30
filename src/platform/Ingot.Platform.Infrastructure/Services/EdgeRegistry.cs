// 持久化边缘节点站点归属、心跳状态和诊断历史。
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Ingot.Platform.Application.Acquisition;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Services;

public sealed class EdgeRegistry(NpgsqlDataSource dataSource) : IEdgeSiteDirectory, IEdgeTaskDeploymentHistoryReader
{
    private const int MaximumDeploymentObservationRows = 40_000;
    private const int MaximumDeploymentObservationEdges = 2_000;
    private const double MaximumTrustedDeploymentReportAgeMs = 30_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<EdgeState>> ListAsync(
        CancellationToken ct = default,
        string? siteId = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(siteId)
            ? """
            SELECT edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                   acquisition_status::text, delivery_status::text, site_id, delivery_status_received_at
            FROM platform_edges ORDER BY last_seen_at DESC
            """
            : """
            SELECT edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                   acquisition_status::text, delivery_status::text, site_id, delivery_status_received_at
            FROM platform_edges WHERE site_id = $1 ORDER BY last_seen_at DESC
            """;
        if (!string.IsNullOrWhiteSpace(siteId))
            command.Parameters.AddWithValue(siteId.Trim());
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeState>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(ReadEdge(reader));
        return values;
    }

    public Task<EdgeState?> FindAsync(string edgeId, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(edgeId)
            ? Task.FromResult<EdgeState?>(null)
            : GetAsync(edgeId.Trim(), ct);

    public async Task<IReadOnlySet<string>> ListEdgeIdsAsync(string siteId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("Edge 站点查询必须指定 siteId。", nameof(siteId));
        var edges = await ListAsync(ct, siteId).ConfigureAwait(false);
        return edges.Select(static edge => edge.EdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<EdgeTaskDeploymentObservationQueryResult> FindOverlappingAsync(
        string siteId,
        IReadOnlyCollection<string> edgeIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(edgeIds);
        if (from > to)
            throw new ArgumentException("任务版本观察范围的开始时间不能晚于结束时间。", nameof(from));
        var scopedEdgeIds = edgeIds
            .Where(static edgeId => !string.IsNullOrWhiteSpace(edgeId))
            .Select(static edgeId => edgeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scopedEdgeIds.Length == 0)
            return new EdgeTaskDeploymentObservationQueryResult([], false);
        if (scopedEdgeIds.Length > MaximumDeploymentObservationEdges)
            return new EdgeTaskDeploymentObservationQueryResult([], true);

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT history.edge_id, history.task_id, history.task_version,
                   history.configuration_hash, history.configuration_source,
                   history.first_observed_at, history.last_observed_at, history.closed_observed_at,
                   history.first_edge_reported_at, history.last_edge_reported_at,
                   history.edge_applied_at_hint, history.last_deployment_state
            FROM edge_task_deployment_observations AS history
            INNER JOIN platform_edges AS edge ON edge.edge_id = history.edge_id
            WHERE edge.site_id = $1
              AND history.edge_id = ANY($2::text[])
              AND history.first_observed_at <= $4
              AND COALESCE(history.closed_observed_at, history.last_observed_at) >= $3
            ORDER BY history.first_observed_at, history.edge_id, history.task_id, history.observation_id
            LIMIT $5
            """;
        command.Parameters.AddWithValue(NormalizeSiteId(siteId));
        command.Parameters.Add("edge_ids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = scopedEdgeIds;
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        command.Parameters.AddWithValue(MaximumDeploymentObservationRows + 1);

        var observations = new List<EdgeTaskDeploymentObservation>(MaximumDeploymentObservationRows);
        var truncated = false;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (observations.Count == MaximumDeploymentObservationRows)
            {
                truncated = true;
                break;
            }
            observations.Add(new EdgeTaskDeploymentObservation
            {
                EdgeId = reader.GetString(0),
                TaskId = reader.GetString(1),
                TaskVersion = reader.GetInt32(2),
                ConfigurationHash = reader.GetString(3),
                ConfigurationSource = reader.GetString(4),
                FirstObservedAt = reader.GetFieldValue<DateTimeOffset>(5),
                LastObservedAt = reader.GetFieldValue<DateTimeOffset>(6),
                ClosedObservedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                FirstEdgeReportedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                LastEdgeReportedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                EdgeAppliedAtHint = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                LastDeploymentState = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }
        return new EdgeTaskDeploymentObservationQueryResult(observations, truncated);
    }

    public async Task<IReadOnlyList<EdgeRuntimeStatusHistoryItem>> ListStatusHistoryAsync(
        string edgeId,
        int limit = 288,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return [];
        limit = Math.Clamp(limit, 1, 1000);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT recorded_at, acquisition_reported_at, acquisition_state, last_valid_snapshot_at,
                   valid_snapshot_count, emitted_event_count, acquisition_error,
                   delivery_state, pending_event_count, oldest_pending_event_at,
                   backlog_capacity_used_percent, shipment_rate_per_second, delivery_error,
                   backlog_observed_at
            FROM edge_runtime_status_history
            WHERE edge_id = $1 ORDER BY recorded_at DESC LIMIT $2
            """;
        command.Parameters.AddWithValue(edgeId.Trim());
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeRuntimeStatusHistoryItem>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var recordedAt = reader.GetFieldValue<DateTimeOffset>(0);
            var reportedBacklogObservedAt = reader.IsDBNull(13)
                ? (DateTimeOffset?)null
                : reader.GetFieldValue<DateTimeOffset>(13);
            var verifiedBacklogObservedAt = reportedBacklogObservedAt.HasValue && reportedBacklogObservedAt.Value <= recordedAt
                ? reportedBacklogObservedAt
                : (DateTimeOffset?)null;
            values.Add(new EdgeRuntimeStatusHistoryItem
            {
                EdgeId = edgeId.Trim(),
                RecordedAt = recordedAt,
                AcquisitionReportedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                AcquisitionState = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastValidSnapshotAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                ValidSnapshotCount = reader.GetInt64(4),
                EmittedEventCount = reader.GetInt64(5),
                AcquisitionError = reader.IsDBNull(6) ? null : reader.GetString(6),
                DeliveryState = reader.IsDBNull(7) ? null : reader.GetString(7),
                PendingEventCount = verifiedBacklogObservedAt.HasValue ? reader.GetInt64(8) : null,
                OldestPendingEventAt = verifiedBacklogObservedAt.HasValue && !reader.IsDBNull(9)
                    ? reader.GetFieldValue<DateTimeOffset>(9)
                    : null,
                BacklogCapacityUsedPercent = verifiedBacklogObservedAt.HasValue && !reader.IsDBNull(10)
                    ? reader.GetDouble(10)
                    : null,
                ShipmentRatePerSecond = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                DeliveryError = reader.IsDBNull(12) ? null : reader.GetString(12),
                BacklogObservedAt = verifiedBacklogObservedAt
            });
        }
        return values;
    }

    public async Task<IReadOnlyList<EdgeRuntimeStatusInterval>> ListStatusIntervalsAsync(
        string edgeId,
        int limit = 24,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return [];
        edgeId = edgeId.Trim();
        limit = Math.Clamp(limit, 1, 200);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH ordered AS (
              SELECT *,
                CASE WHEN
                  acquisition_state IS DISTINCT FROM lag(acquisition_state) OVER sequence OR
                  acquisition_error IS DISTINCT FROM lag(acquisition_error) OVER sequence OR
                  delivery_state IS DISTINCT FROM lag(delivery_state) OVER sequence OR
                  delivery_error IS DISTINCT FROM lag(delivery_error) OVER sequence
                THEN 1 ELSE 0 END AS changed
              FROM edge_runtime_status_history
              WHERE edge_id = $1
              WINDOW sequence AS (ORDER BY recorded_at)
            ), grouped AS (
              SELECT *, sum(changed) OVER (ORDER BY recorded_at) AS interval_id
              FROM ordered
            ), sequenced AS (
              SELECT *, min(recorded_at) OVER (PARTITION BY interval_id) AS interval_started_at
              FROM grouped
            )
            SELECT min(recorded_at), max(recorded_at), count(*),
                   acquisition_state, acquisition_error, delivery_state, delivery_error,
                   (array_agg(valid_snapshot_count ORDER BY recorded_at))[1],
                   (array_agg(valid_snapshot_count ORDER BY recorded_at DESC))[1],
                   (array_agg(emitted_event_count ORDER BY recorded_at))[1],
                   (array_agg(emitted_event_count ORDER BY recorded_at DESC))[1],
                   max(pending_event_count) FILTER (WHERE backlog_observed_at IS NOT NULL AND backlog_observed_at >= interval_started_at AND backlog_observed_at <= recorded_at),
                   count(*) FILTER (WHERE backlog_observed_at IS NOT NULL AND backlog_observed_at >= interval_started_at AND backlog_observed_at <= recorded_at),
                   count(DISTINCT backlog_observed_at) FILTER (WHERE backlog_observed_at >= interval_started_at AND backlog_observed_at <= recorded_at),
                   max(backlog_observed_at) FILTER (WHERE backlog_observed_at >= interval_started_at AND backlog_observed_at <= recorded_at)
            FROM sequenced
            GROUP BY interval_id, interval_started_at, acquisition_state, acquisition_error, delivery_state, delivery_error
            ORDER BY max(recorded_at) DESC
            LIMIT $2
            """;
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeRuntimeStatusInterval>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            values.Add(new EdgeRuntimeStatusInterval
            {
                EdgeId = edgeId,
                StartedAt = reader.GetFieldValue<DateTimeOffset>(0),
                EndedAt = reader.GetFieldValue<DateTimeOffset>(1),
                SampleCount = reader.GetInt64(2),
                AcquisitionState = reader.IsDBNull(3) ? null : reader.GetString(3),
                AcquisitionError = reader.IsDBNull(4) ? null : reader.GetString(4),
                DeliveryState = reader.IsDBNull(5) ? null : reader.GetString(5),
                DeliveryError = reader.IsDBNull(6) ? null : reader.GetString(6),
                StartingValidSnapshotCount = reader.GetInt64(7),
                EndingValidSnapshotCount = reader.GetInt64(8),
                StartingEmittedEventCount = reader.GetInt64(9),
                EndingEmittedEventCount = reader.GetInt64(10),
                MaximumPendingEventCount = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                BacklogObservationHeartbeatCount = reader.GetInt64(12),
                DistinctBacklogObservationCount = reader.GetInt64(13),
                LastBacklogObservedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14)
            });
        }
        return values;
    }

    public async Task<IReadOnlyList<EdgeTaskFreshnessTransition>> ListTaskFreshnessTransitionsAsync(
        string edgeId,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return [];
        limit = Math.Clamp(limit, 1, 1000);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT configuration_key, previous_state, state,
                   previous_threshold_ms, threshold_ms,
                   effective_at, detected_at, duration_ms, last_valid_snapshot_at
            FROM edge_task_freshness_transitions
            WHERE edge_id = $1
            ORDER BY detected_at DESC, transition_id DESC
            LIMIT $2
            """;
        command.Parameters.AddWithValue(edgeId.Trim());
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeTaskFreshnessTransition>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            values.Add(new EdgeTaskFreshnessTransition
            {
                EdgeId = edgeId.Trim(),
                ConfigurationKey = reader.GetString(0),
                PreviousState = reader.IsDBNull(1) ? null : reader.GetString(1),
                State = reader.GetString(2),
                PreviousThresholdMs = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ThresholdMs = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                EffectiveAt = reader.GetFieldValue<DateTimeOffset>(5),
                DetectedAt = reader.GetFieldValue<DateTimeOffset>(6),
                DurationMs = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                LastValidSnapshotAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)
            });
        }
        return values;
    }

    public async Task<EdgeTaskFreshnessEvaluationSummary> EvaluateTaskFreshnessTransitionsAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        var edgeIds = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT edge_id FROM platform_edges WHERE acquisition_status IS NOT NULL ORDER BY edge_id";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                edgeIds.Add(reader.GetString(0));
        }

        var evaluatedEdgeCount = 0;
        var evaluatedTaskCount = 0;
        var transitionCount = 0;
        foreach (var edgeId in edgeIds)
        {
            ct.ThrowIfCancellationRequested();
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            EdgeAcquisitionRuntimeStatus? acquisition;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT acquisition_status::text FROM platform_edges WHERE edge_id = $1 FOR UPDATE";
                command.Parameters.AddWithValue(edgeId);
                var raw = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
                acquisition = raw is null ? null : JsonSerializer.Deserialize<EdgeAcquisitionRuntimeStatus>(raw, JsonOptions);
            }

            if (acquisition is null || acquisition.Tasks is null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                continue;
            }

            evaluatedEdgeCount++;
            // Heartbeats can arrive after the worker pass starts but before it locks this edge.
            // Never evaluate or record a transition before the report was received.
            var observedAt = acquisition.PlatformReceivedAt is { } receivedAt && receivedAt > now
                ? receivedAt
                : now;
            var currentStates = await LoadTaskFreshnessCurrentAsync(connection, transaction, edgeId, ct)
                .ConfigureAwait(false);
            var presentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in acquisition.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.ConfigurationKey))
                    continue;
                evaluatedTaskCount++;
                presentKeys.Add(task.ConfigurationKey);
                currentStates.TryGetValue(task.ConfigurationKey, out var previous);
                var evaluation = EdgeTaskFreshnessEvaluator.Evaluate(task, acquisition, observedAt);
                var transition = EdgeTaskFreshnessEvaluator.DecideTransition(
                    previous?.State,
                    previous?.ThresholdMs,
                    previous?.StateSince,
                    evaluation,
                    acquisition,
                    observedAt);

                if (transition.Changed)
                {
                    await InsertTaskFreshnessTransitionAsync(
                        connection,
                        transaction,
                        edgeId,
                        task.ConfigurationKey,
                        previous?.State,
                        evaluation.State,
                        previous?.ThresholdMs,
                        evaluation.ThresholdMs,
                        transition.EffectiveAt,
                        observedAt,
                        transition.DurationMs,
                        task.LastValidSnapshotAt,
                        ct).ConfigureAwait(false);
                    await UpsertTaskFreshnessCurrentAsync(
                        connection,
                        transaction,
                        edgeId,
                        task.ConfigurationKey,
                        evaluation.State,
                        evaluation.ThresholdMs,
                        transition.StateSince,
                        observedAt,
                        ct).ConfigureAwait(false);
                    transitionCount++;
                }
            }

            foreach (var previous in currentStates.Values.Where(state => !presentKeys.Contains(state.ConfigurationKey)))
            {
                if (previous.State == EdgeTaskFreshnessStates.Removed)
                    continue;
                await InsertTaskFreshnessTransitionAsync(
                    connection,
                    transaction,
                    edgeId,
                    previous.ConfigurationKey,
                    previous.State,
                    EdgeTaskFreshnessStates.Removed,
                    previous.ThresholdMs,
                    previous.ThresholdMs,
                    observedAt,
                    observedAt,
                    null,
                    null,
                    ct).ConfigureAwait(false);
                await UpsertTaskFreshnessCurrentAsync(
                    connection,
                    transaction,
                    edgeId,
                    previous.ConfigurationKey,
                    EdgeTaskFreshnessStates.Removed,
                    previous.ThresholdMs,
                    observedAt,
                    observedAt,
                    ct).ConfigureAwait(false);
                transitionCount++;
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        return new EdgeTaskFreshnessEvaluationSummary(evaluatedEdgeCount, evaluatedTaskCount, transitionCount);
    }

    public async Task PruneRuntimeStatusHistoryAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-7);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM edge_runtime_status_history WHERE recorded_at < $1";
            command.Parameters.AddWithValue(cutoff);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM edge_task_freshness_transitions WHERE detected_at < $1";
            command.Parameters.AddWithValue(cutoff);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM edge_task_freshness_current WHERE state = 'removed' AND updated_at < $1";
            command.Parameters.AddWithValue(cutoff);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, TaskFreshnessCurrent>> LoadTaskFreshnessCurrentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT configuration_key, state, threshold_ms, state_since
            FROM edge_task_freshness_current
            WHERE edge_id = $1
            """;
        command.Parameters.AddWithValue(edgeId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new Dictionary<string, TaskFreshnessCurrent>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var configurationKey = reader.GetString(0);
            values[configurationKey] = new TaskFreshnessCurrent(
                configurationKey,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3));
        }
        return values;
    }

    private static async Task InsertTaskFreshnessTransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        string configurationKey,
        string? previousState,
        string state,
        int? previousThresholdMs,
        int? thresholdMs,
        DateTimeOffset effectiveAt,
        DateTimeOffset detectedAt,
        long? durationMs,
        DateTimeOffset? lastValidSnapshotAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO edge_task_freshness_transitions(
              edge_id, configuration_key, previous_state, state,
              previous_threshold_ms, threshold_ms,
              effective_at, detected_at, duration_ms, last_valid_snapshot_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """;
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(configurationKey);
        AddNullable(command, NpgsqlDbType.Text, previousState);
        command.Parameters.AddWithValue(state);
        AddNullable(command, NpgsqlDbType.Integer, previousThresholdMs);
        AddNullable(command, NpgsqlDbType.Integer, thresholdMs);
        command.Parameters.AddWithValue(effectiveAt);
        command.Parameters.AddWithValue(detectedAt);
        AddNullable(command, NpgsqlDbType.Bigint, durationMs);
        AddNullable(command, NpgsqlDbType.TimestampTz, lastValidSnapshotAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertTaskFreshnessCurrentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        string configurationKey,
        string state,
        int? thresholdMs,
        DateTimeOffset stateSince,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO edge_task_freshness_current(
              edge_id, configuration_key, state, threshold_ms, state_since, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT(edge_id, configuration_key) DO UPDATE SET
              state = EXCLUDED.state,
              threshold_ms = EXCLUDED.threshold_ms,
              state_since = EXCLUDED.state_since,
              updated_at = EXCLUDED.updated_at
            """;
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(configurationKey);
        command.Parameters.AddWithValue(state);
        AddNullable(command, NpgsqlDbType.Integer, thresholdMs);
        command.Parameters.AddWithValue(stateSince);
        command.Parameters.AddWithValue(updatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record TaskFreshnessCurrent(
        string ConfigurationKey,
        string State,
        int? ThresholdMs,
        DateTimeOffset StateSince);

    public async Task<EdgeState> UpsertAsync(
        string edgeId,
        string siteId,
        string? hostBaseUrl,
        string? hostname,
        string? version,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        edgeId = edgeId.Trim();
        siteId = NormalizeSiteId(siteId);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO platform_edges(
              edge_id, site_id, host_base_url, hostname, version, last_seen_at, last_error)
            VALUES ($1, $2, $3, $4, $5, $6, NULL)
            ON CONFLICT(edge_id) DO UPDATE SET
              host_base_url = COALESCE(EXCLUDED.host_base_url, platform_edges.host_base_url),
              hostname = COALESCE(EXCLUDED.hostname, platform_edges.hostname),
              version = COALESCE(EXCLUDED.version, platform_edges.version),
              last_seen_at = EXCLUDED.last_seen_at
            WHERE platform_edges.site_id = EXCLUDED.site_id
            """;
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(siteId);
        AddNullable(command, NpgsqlDbType.Text, NormalizeBaseUrl(hostBaseUrl));
        AddNullable(command, NpgsqlDbType.Text, hostname);
        AddNullable(command, NpgsqlDbType.Text, version);
        command.Parameters.AddWithValue(now);
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var stored = await GetAsync(edgeId, ct).ConfigureAwait(false);
        if (affected == 0 && stored is not null && !string.Equals(
                stored.SiteId, siteId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("边缘节点不能从原站点迁移到另一个站点。");
        return stored
            ?? throw new InvalidOperationException("边缘节点注册写入后无法读取。");
    }

    public async Task<EdgeState> HeartbeatAsync(
        string edgeId,
        string siteId,
        string? hostBaseUrl,
        string? lastError,
        EdgeAcquisitionRuntimeStatus? acquisition,
        DateTimeOffset now,
        EdgeDeliveryRuntimeStatus? delivery = null,
        CancellationToken ct = default)
    {
        edgeId = edgeId.Trim();
        siteId = NormalizeSiteId(siteId);
        var recordedAcquisition = acquisition is null
            ? null
            : acquisition with { PlatformReceivedAt = now, PlatformObservedAt = null };
        var recordedDelivery = RebaseBacklogObservation(delivery, now).Status;
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO platform_edges(
                  edge_id, site_id, host_base_url, hostname, version, last_seen_at, last_error,
                  acquisition_status, delivery_status, delivery_status_received_at)
                VALUES ($1, $2, $3, NULL, NULL, $4, $5, $6, $7, $8)
                ON CONFLICT(edge_id) DO UPDATE SET
                  last_seen_at = EXCLUDED.last_seen_at,
                  host_base_url = COALESCE(EXCLUDED.host_base_url, platform_edges.host_base_url),
                  last_error = EXCLUDED.last_error,
                  acquisition_status = COALESCE(EXCLUDED.acquisition_status, platform_edges.acquisition_status),
                  delivery_status = COALESCE(EXCLUDED.delivery_status, platform_edges.delivery_status),
                  delivery_status_received_at = COALESCE(
                    EXCLUDED.delivery_status_received_at,
                    platform_edges.delivery_status_received_at)
                WHERE platform_edges.site_id = EXCLUDED.site_id
                """;
            command.Parameters.AddWithValue(edgeId);
            command.Parameters.AddWithValue(siteId);
            AddNullable(command, NpgsqlDbType.Text, NormalizeBaseUrl(hostBaseUrl));
            command.Parameters.AddWithValue(now);
            AddNullable(command, NpgsqlDbType.Text, lastError);
            AddJson(command, recordedAcquisition);
            AddJson(command, delivery);
            AddNullable(command, NpgsqlDbType.TimestampTz, delivery is null ? null : now);
            var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException("边缘节点不能从原站点迁移到另一个站点。");
        }
        if (recordedAcquisition is not null || recordedDelivery is not null)
            await SaveStatusHistoryAsync(connection, transaction, edgeId, recordedAcquisition, recordedDelivery, now, ct)
                .ConfigureAwait(false);
        if (recordedAcquisition is not null)
            await CaptureTaskDeploymentObservationsAsync(
                    connection, transaction, edgeId, recordedAcquisition, now, ct)
                .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await GetAsync(edgeId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("边缘节点心跳写入后无法读取。");
    }

    // Stores only fresh, source-qualified AppliedVersion/hash reports; Desired/Pending/Failed never replace applied identity.
    private static async Task CaptureTaskDeploymentObservationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        EdgeAcquisitionRuntimeStatus acquisition,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        if (!IsTrustedDeploymentReport(acquisition, receivedAt))
            return;

        var source = acquisition.ConfigurationSource!.Trim().ToLowerInvariant();
        var reportedAt = acquisition.ReportedAt;
        await using (var watermark = connection.CreateCommand())
        {
            watermark.Transaction = transaction;
            watermark.CommandText =
                "SELECT last_trusted_edge_reported_at FROM edge_task_deployment_observation_state WHERE edge_id = $1 FOR UPDATE";
            watermark.Parameters.AddWithValue(edgeId);
            await using var reader = await watermark.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false) &&
                reportedAt < reader.GetFieldValue<DateTimeOffset>(0))
                return;
        }

        var active = await LoadActiveTaskDeploymentObservationsAsync(
                connection, transaction, edgeId, ct)
            .ConfigureAwait(false);
        var deployments = acquisition.Deployments;
        IGrouping<string, AcquisitionDeploymentApplicationStatus>[] taskGroups = deployments is null
            ? []
            : deployments
                .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.TaskId))
                .GroupBy(static item => item.TaskId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var completeSnapshot =
            !string.IsNullOrWhiteSpace(acquisition.DesiredConfigurationSetHash) &&
            deployments is not null &&
            taskGroups.Length == deployments.Count &&
            taskGroups.All(static group => group.Count() == 1);
        var presentTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in taskGroups)
        {
            if (group.Count() != 1)
                continue;
            var deployment = group.Single();
            var taskId = deployment.TaskId.Trim().ToLowerInvariant();
            presentTaskIds.Add(taskId);
            if (deployment.AppliedVersion is not > 0 ||
                string.IsNullOrWhiteSpace(deployment.AppliedConfigurationHash))
                continue;

            var hash = deployment.AppliedConfigurationHash.Trim();
            if (active.TryGetValue(taskId, out var previous) &&
                previous.TaskVersion == deployment.AppliedVersion.Value &&
                string.Equals(previous.ConfigurationHash, hash, StringComparison.Ordinal))
            {
                await UpdateTaskDeploymentObservationAsync(
                        connection, transaction, edgeId, taskId, hash, reportedAt,
                        deployment, receivedAt, ct)
                    .ConfigureAwait(false);
                continue;
            }

            if (previous is not null)
            {
                await CloseTaskDeploymentObservationAsync(
                        connection, transaction, edgeId, taskId, receivedAt, ct)
                    .ConfigureAwait(false);
                active.Remove(taskId);
            }

            await InsertTaskDeploymentObservationAsync(
                    connection, transaction, edgeId, taskId, deployment.AppliedVersion.Value,
                    hash, source, reportedAt, deployment, receivedAt, ct)
                .ConfigureAwait(false);
            active[taskId] = new ActiveTaskDeploymentObservation(
                deployment.AppliedVersion.Value, hash);
        }

        if (completeSnapshot)
        {
            foreach (var taskId in active.Keys.Where(taskId => !presentTaskIds.Contains(taskId)).ToArray())
            {
                await CloseTaskDeploymentObservationAsync(
                        connection, transaction, edgeId, taskId, receivedAt, ct)
                    .ConfigureAwait(false);
                active.Remove(taskId);
            }
        }

        await using var updateWatermark = connection.CreateCommand();
        updateWatermark.Transaction = transaction;
        updateWatermark.CommandText =
            "INSERT INTO edge_task_deployment_observation_state(edge_id, last_trusted_edge_reported_at, updated_at) " +
            "VALUES ($1, $2, $3) ON CONFLICT(edge_id) DO UPDATE SET " +
            "last_trusted_edge_reported_at = GREATEST(edge_task_deployment_observation_state.last_trusted_edge_reported_at, EXCLUDED.last_trusted_edge_reported_at), " +
            "updated_at = EXCLUDED.updated_at";
        updateWatermark.Parameters.AddWithValue(edgeId);
        updateWatermark.Parameters.AddWithValue(reportedAt);
        updateWatermark.Parameters.AddWithValue(receivedAt);
        await updateWatermark.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ReportedAt orders Edge snapshots; Platform receipt time is the only observation boundary.
    private static bool IsTrustedDeploymentReport(
        EdgeAcquisitionRuntimeStatus acquisition,
        DateTimeOffset receivedAt)
    {
        if (acquisition.ReportedAt == default ||
            acquisition.ReportedAt > receivedAt ||
            receivedAt - acquisition.ReportedAt > TimeSpan.FromMilliseconds(MaximumTrustedDeploymentReportAgeMs))
            return false;
        return string.Equals(acquisition.ConfigurationSource?.Trim(), "platform", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(acquisition.ConfigurationSource?.Trim(), "cache", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, ActiveTaskDeploymentObservation>> LoadActiveTaskDeploymentObservationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT task_id, task_version, configuration_hash " +
            "FROM edge_task_deployment_observations WHERE edge_id = $1 AND closed_observed_at IS NULL FOR UPDATE";
        command.Parameters.AddWithValue(edgeId);
        var active = new Dictionary<string, ActiveTaskDeploymentObservation>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            active[reader.GetString(0)] = new ActiveTaskDeploymentObservation(
                reader.GetInt32(1), reader.GetString(2));
        }
        return active;
    }

    private static async Task UpdateTaskDeploymentObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        string taskId,
        string hash,
        DateTimeOffset reportedAt,
        AcquisitionDeploymentApplicationStatus deployment,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE edge_task_deployment_observations SET last_observed_at = $5, " +
            "last_edge_reported_at = $6, edge_applied_at_hint = COALESCE($7, edge_applied_at_hint), " +
            "last_deployment_state = $8 " +
            "WHERE edge_id = $1 AND task_id = $2 AND task_version = $3 " +
            "AND configuration_hash = $4 AND closed_observed_at IS NULL";
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(deployment.AppliedVersion!.Value);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(receivedAt);
        command.Parameters.AddWithValue(reportedAt);
        AddNullable(command, NpgsqlDbType.TimestampTz, ValidTimestampHint(deployment.AppliedAt, reportedAt));
        AddNullable(command, NpgsqlDbType.Text, deployment.State);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertTaskDeploymentObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        string taskId,
        int taskVersion,
        string hash,
        string source,
        DateTimeOffset reportedAt,
        AcquisitionDeploymentApplicationStatus deployment,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO edge_task_deployment_observations(" +
            "edge_id, task_id, task_version, configuration_hash, configuration_source, " +
            "first_observed_at, last_observed_at, first_edge_reported_at, last_edge_reported_at, " +
            "edge_applied_at_hint, last_deployment_state) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $6, $7, $7, $8, $9)";
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(taskVersion);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(source);
        command.Parameters.AddWithValue(receivedAt);
        command.Parameters.AddWithValue(reportedAt);
        AddNullable(command, NpgsqlDbType.TimestampTz, ValidTimestampHint(deployment.AppliedAt, reportedAt));
        AddNullable(command, NpgsqlDbType.Text, deployment.State);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task CloseTaskDeploymentObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        string taskId,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE edge_task_deployment_observations SET closed_observed_at = $3 " +
            "WHERE edge_id = $1 AND task_id = $2 AND closed_observed_at IS NULL";
        command.Parameters.AddWithValue(edgeId);
        command.Parameters.AddWithValue(taskId);
        command.Parameters.AddWithValue(receivedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static DateTimeOffset? ValidTimestampHint(DateTimeOffset? hint, DateTimeOffset reportedAt)
        => hint is { } timestamp && timestamp != default && timestamp <= reportedAt
            ? timestamp
            : null;

    private sealed record ActiveTaskDeploymentObservation(int TaskVersion, string ConfigurationHash);

    private async Task<EdgeState?> GetAsync(string edgeId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                   acquisition_status::text, delivery_status::text, site_id, delivery_status_received_at
            FROM platform_edges WHERE edge_id = $1
            """;
        command.Parameters.AddWithValue(edgeId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadEdge(reader) : null;
    }

    private static EdgeState ReadEdge(NpgsqlDataReader reader)
    {
        var now = DateTimeOffset.UtcNow;
        var lastSeen = reader.GetFieldValue<DateTimeOffset>(4);
        var heartbeatAge = now - lastSeen;
        bool? heartbeatFresh = heartbeatAge >= TimeSpan.Zero
            ? heartbeatAge <= TimeSpan.FromSeconds(30)
            : null;
        var deliveryReceivedAt = reader.IsDBNull(9)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(9);
        var storedDelivery = Deserialize<EdgeDeliveryRuntimeStatus>(reader, 7);
        var rebasedDelivery = deliveryReceivedAt.HasValue
            ? RebaseBacklogObservation(storedDelivery, deliveryReceivedAt.Value)
            : (Status: WithoutVerifiedBacklog(storedDelivery), Invalid: false);
        var delivery = rebasedDelivery.Status;
        var backlogObservationInvalid = rebasedDelivery.Invalid;
        var backlogAge = delivery?.BacklogObservedAt is { } observedAt ? now - observedAt : (TimeSpan?)null;
        var deliveryReportAge = deliveryReceivedAt.HasValue ? now - deliveryReceivedAt.Value : (TimeSpan?)null;
        var backlogAgeAtDeliveryReport = delivery?.BacklogObservedAt is { } reportedAt && deliveryReceivedAt.HasValue
            ? deliveryReceivedAt.Value - reportedAt
            : (TimeSpan?)null;
        var backlogStatisticsFresh = heartbeatFresh == true
            && deliveryReportAge.HasValue && deliveryReportAge.Value >= TimeSpan.Zero
            && deliveryReportAge.Value <= TimeSpan.FromSeconds(30)
            && backlogAge.HasValue && backlogAge.Value >= TimeSpan.Zero && backlogAge.Value <= TimeSpan.FromSeconds(30)
            && backlogAgeAtDeliveryReport.HasValue && backlogAgeAtDeliveryReport.Value >= TimeSpan.Zero
            && backlogAgeAtDeliveryReport.Value <= TimeSpan.FromSeconds(30);

        return new EdgeState(reader.GetString(0))
        {
            HostBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
            Hostname = reader.IsDBNull(2) ? null : reader.GetString(2),
            Version = reader.IsDBNull(3) ? null : reader.GetString(3),
            LastSeen = lastSeen,
            ServerObservedAt = now,
            HeartbeatFresh = heartbeatFresh,
            BacklogStatisticsFresh = backlogStatisticsFresh,
            BacklogStatisticsAgeMs = backlogAge.HasValue && backlogAge.Value >= TimeSpan.Zero
                ? backlogAge.Value.TotalMilliseconds
                : null,
            BacklogObservationInvalid = backlogObservationInvalid,
            LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
            Acquisition = Deserialize<EdgeAcquisitionRuntimeStatus>(reader, 6) is { } acquisition
                ? acquisition with { PlatformObservedAt = now }
                : null,
            Delivery = delivery,
            SiteId = reader.GetString(8)
        };
    }

    private static async Task SaveStatusHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        EdgeAcquisitionRuntimeStatus? acquisition,
        EdgeDeliveryRuntimeStatus? delivery,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO edge_runtime_status_history(
                  edge_id, recorded_at, acquisition_reported_at, acquisition_state, last_valid_snapshot_at,
                  valid_snapshot_count, emitted_event_count, acquisition_error,
                  delivery_state, pending_event_count, oldest_pending_event_at,
                  backlog_capacity_used_percent, shipment_rate_per_second, delivery_error,
                  backlog_observed_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15)
                ON CONFLICT(edge_id, recorded_at) DO UPDATE SET
                  acquisition_reported_at = EXCLUDED.acquisition_reported_at,
                  acquisition_state = EXCLUDED.acquisition_state,
                  last_valid_snapshot_at = EXCLUDED.last_valid_snapshot_at,
                  valid_snapshot_count = EXCLUDED.valid_snapshot_count,
                  emitted_event_count = EXCLUDED.emitted_event_count,
                  acquisition_error = EXCLUDED.acquisition_error,
                  delivery_state = EXCLUDED.delivery_state,
                  pending_event_count = EXCLUDED.pending_event_count,
                  oldest_pending_event_at = EXCLUDED.oldest_pending_event_at,
                  backlog_capacity_used_percent = EXCLUDED.backlog_capacity_used_percent,
                  shipment_rate_per_second = EXCLUDED.shipment_rate_per_second,
                  delivery_error = EXCLUDED.delivery_error,
                  backlog_observed_at = EXCLUDED.backlog_observed_at
                """;
            command.Parameters.AddWithValue(edgeId);
            command.Parameters.AddWithValue(receivedAt);
            AddNullable(command, NpgsqlDbType.TimestampTz, acquisition?.ReportedAt);
            AddNullable(command, NpgsqlDbType.Text, acquisition?.State);
            AddNullable(command, NpgsqlDbType.TimestampTz, acquisition?.LastValidSnapshotAt);
            command.Parameters.AddWithValue(acquisition?.ValidSnapshotCount ?? 0);
            command.Parameters.AddWithValue(acquisition?.EmittedEventCount ?? 0);
            AddNullable(command, NpgsqlDbType.Text, acquisition?.LastError);
            AddNullable(command, NpgsqlDbType.Text, delivery?.State);
            command.Parameters.AddWithValue(delivery?.PendingEventCount ?? 0);
            AddNullable(command, NpgsqlDbType.TimestampTz, delivery?.OldestPendingEventAt);
            AddNullable(command, NpgsqlDbType.Double, delivery?.BacklogCapacityUsedPercent);
            AddNullable(command, NpgsqlDbType.Double, delivery?.ShipmentRatePerSecond);
            AddNullable(command, NpgsqlDbType.Text, delivery?.LastError);
            AddNullable(command, NpgsqlDbType.TimestampTz, delivery?.BacklogObservedAt);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText =
            "DELETE FROM edge_runtime_status_history WHERE edge_id = $1 AND recorded_at < $2";
        prune.Parameters.AddWithValue(edgeId);
        prune.Parameters.AddWithValue(receivedAt.AddDays(-7));
        await prune.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // 用 Edge 报告内的经过时间重建 Platform 时间，避免跨主机直接比较本地时钟。
    private static (EdgeDeliveryRuntimeStatus? Status, bool Invalid) RebaseBacklogObservation(
        EdgeDeliveryRuntimeStatus? delivery,
        DateTimeOffset receivedAt)
    {
        if (delivery is null) return (null, false);
        TimeSpan age;
        TimeSpan? oldestPendingAgeAtObservation = null;
        if (delivery.BacklogObservationAgeMs is { } ageMs)
        {
            if (!double.IsFinite(ageMs) || ageMs < 0 || ageMs > TimeSpan.MaxValue.TotalMilliseconds)
                return (WithoutVerifiedBacklog(delivery), true);
            try
            {
                age = TimeSpan.FromMilliseconds(ageMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                return (WithoutVerifiedBacklog(delivery), true);
            }
        }
        else if (delivery.BacklogObservedAt is { } reportedAt)
        {
            // Older Edge versions provide only two timestamps from the same local clock.
            age = delivery.ObservedAt - reportedAt;
            if (age < TimeSpan.Zero)
                return (WithoutVerifiedBacklog(delivery), true);
        }
        else
        {
            return (WithoutVerifiedBacklog(delivery), false);
        }

        if (delivery.BacklogObservedAt is { } backlogObservedAt
            && delivery.OldestPendingEventAt is { } reportedOldestAt)
        {
            var oldestAge = backlogObservedAt - reportedOldestAt;
            if (oldestAge >= TimeSpan.Zero) oldestPendingAgeAtObservation = oldestAge;
        }

        try
        {
            var oldestPendingAt = oldestPendingAgeAtObservation is { } oldestAgeAtObservation
                ? receivedAt - age - oldestAgeAtObservation
                : (DateTimeOffset?)null;
            return (delivery with
            {
                BacklogObservedAt = receivedAt - age,
                OldestPendingEventAt = oldestPendingAt
            }, false);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (WithoutVerifiedBacklog(delivery), true);
        }
    }

    // Hide queue-derived values whenever their source observation cannot be placed on the Platform clock.
    private static EdgeDeliveryRuntimeStatus? WithoutVerifiedBacklog(EdgeDeliveryRuntimeStatus? delivery)
        => delivery is null
            ? null
            : delivery with
            {
                BacklogObservedAt = null,
                BacklogObservationAgeMs = null,
                PendingEventCount = null,
                OldestPendingEventAt = null,
                BacklogCapacityRows = null,
                BacklogCapacityUsedPercent = null,
                LocalStorageBytes = null,
                EstimatedDrainSeconds = null
            };

    private static void AddJson<T>(NpgsqlCommand command, T? value) where T : class
        => command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, JsonOptions)
        });

    private static void AddNullable(NpgsqlCommand command, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = type,
            Value = value ?? DBNull.Value
        });

    private static T? Deserialize<T>(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? default
            : JsonSerializer.Deserialize<T>(reader.GetString(ordinal), JsonOptions);

    private static string? NormalizeBaseUrl(string? url)
        => string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

    private static string NormalizeSiteId(string? siteId)
    {
        siteId = siteId?.Trim();
        if (string.IsNullOrWhiteSpace(siteId) || siteId.Length > 128)
            throw new ArgumentException("SiteId 不能为空且最长 128 个字符。", nameof(siteId));
        return siteId;
    }

    public sealed class EdgeState(string edgeId)
    {
        public string EdgeId { get; } = edgeId;
        public required string SiteId { get; set; }
        public string? HostBaseUrl { get; set; }
        public string? Hostname { get; set; }
        public string? Version { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public DateTimeOffset ServerObservedAt { get; set; }
        public bool? HeartbeatFresh { get; set; }
        public bool BacklogStatisticsFresh { get; set; }
        public double? BacklogStatisticsAgeMs { get; set; }
        public bool BacklogObservationInvalid { get; set; }
        public string? LastError { get; set; }
        public EdgeAcquisitionRuntimeStatus? Acquisition { get; set; }
        public EdgeDeliveryRuntimeStatus? Delivery { get; set; }
    }
}
