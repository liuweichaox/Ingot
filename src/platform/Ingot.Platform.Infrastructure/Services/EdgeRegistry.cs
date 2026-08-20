using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Services;

/// <summary>所有 Platform API 副本共享的 PostgreSQL 边缘节点注册表。</summary>
public sealed class EdgeRegistry(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<EdgeState>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                   acquisition_status::text, delivery_status::text
            FROM platform_edges ORDER BY last_seen_at DESC
            """;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeState>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(ReadEdge(reader));
        return values;
    }

    public Task<EdgeState?> FindAsync(string edgeId, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(edgeId)
            ? Task.FromResult<EdgeState?>(null)
            : GetAsync(edgeId.Trim(), ct);

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
            SELECT recorded_at, acquisition_state, last_valid_snapshot_at,
                   valid_snapshot_count, emitted_event_count, acquisition_error,
                   delivery_state, pending_event_count, oldest_pending_event_at,
                   backlog_capacity_used_percent, shipment_rate_per_second, delivery_error
            FROM edge_runtime_status_history
            WHERE edge_id = $1 ORDER BY recorded_at DESC LIMIT $2
            """;
        command.Parameters.AddWithValue(edgeId.Trim());
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<EdgeRuntimeStatusHistoryItem>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            values.Add(new EdgeRuntimeStatusHistoryItem
            {
                EdgeId = edgeId.Trim(),
                RecordedAt = reader.GetFieldValue<DateTimeOffset>(0),
                AcquisitionState = reader.IsDBNull(1) ? null : reader.GetString(1),
                LastValidSnapshotAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                ValidSnapshotCount = reader.GetInt64(3),
                EmittedEventCount = reader.GetInt64(4),
                AcquisitionError = reader.IsDBNull(5) ? null : reader.GetString(5),
                DeliveryState = reader.IsDBNull(6) ? null : reader.GetString(6),
                PendingEventCount = reader.GetInt64(7),
                OldestPendingEventAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                BacklogCapacityUsedPercent = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                ShipmentRatePerSecond = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                DeliveryError = reader.IsDBNull(11) ? null : reader.GetString(11)
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
            )
            SELECT min(recorded_at), max(recorded_at), count(*),
                   acquisition_state, acquisition_error, delivery_state, delivery_error,
                   (array_agg(valid_snapshot_count ORDER BY recorded_at))[1],
                   (array_agg(valid_snapshot_count ORDER BY recorded_at DESC))[1],
                   (array_agg(emitted_event_count ORDER BY recorded_at))[1],
                   (array_agg(emitted_event_count ORDER BY recorded_at DESC))[1],
                   max(pending_event_count)
            FROM grouped
            GROUP BY interval_id, acquisition_state, acquisition_error, delivery_state, delivery_error
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
                MaximumPendingEventCount = reader.GetInt64(11)
            });
        }
        return values;
    }

    public async Task<EdgeState> UpsertAsync(
        string edgeId,
        string? hostBaseUrl,
        string? hostname,
        string? version,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        edgeId = edgeId.Trim();
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO platform_edges(
              edge_id, host_base_url, hostname, version, last_seen_at, last_error)
            VALUES ($1, $2, $3, $4, $5, NULL)
            ON CONFLICT(edge_id) DO UPDATE SET
              host_base_url = COALESCE(EXCLUDED.host_base_url, platform_edges.host_base_url),
              hostname = COALESCE(EXCLUDED.hostname, platform_edges.hostname),
              version = COALESCE(EXCLUDED.version, platform_edges.version),
              last_seen_at = EXCLUDED.last_seen_at
            """;
        command.Parameters.AddWithValue(edgeId);
        AddNullable(command, NpgsqlDbType.Text, NormalizeBaseUrl(hostBaseUrl));
        AddNullable(command, NpgsqlDbType.Text, hostname);
        AddNullable(command, NpgsqlDbType.Text, version);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return await GetAsync(edgeId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("边缘节点注册写入后无法读取。");
    }

    public async Task<EdgeState> HeartbeatAsync(
        string edgeId,
        string? hostBaseUrl,
        string? lastError,
        EdgeAcquisitionRuntimeStatus? acquisition,
        DateTimeOffset now,
        EdgeDeliveryRuntimeStatus? delivery = null,
        CancellationToken ct = default)
    {
        edgeId = edgeId.Trim();
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO platform_edges(
                  edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                  acquisition_status, delivery_status)
                VALUES ($1, $2, NULL, NULL, $3, $4, $5, $6)
                ON CONFLICT(edge_id) DO UPDATE SET
                  last_seen_at = EXCLUDED.last_seen_at,
                  host_base_url = COALESCE(EXCLUDED.host_base_url, platform_edges.host_base_url),
                  last_error = EXCLUDED.last_error,
                  acquisition_status = COALESCE(EXCLUDED.acquisition_status, platform_edges.acquisition_status),
                  delivery_status = COALESCE(EXCLUDED.delivery_status, platform_edges.delivery_status)
                """;
            command.Parameters.AddWithValue(edgeId);
            AddNullable(command, NpgsqlDbType.Text, NormalizeBaseUrl(hostBaseUrl));
            command.Parameters.AddWithValue(now);
            AddNullable(command, NpgsqlDbType.Text, lastError);
            AddJson(command, acquisition);
            AddJson(command, delivery);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        if (acquisition is not null || delivery is not null)
            await SaveStatusHistoryAsync(connection, transaction, edgeId, acquisition, delivery, now, ct)
                .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await GetAsync(edgeId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("边缘节点心跳写入后无法读取。");
    }

    private async Task<EdgeState?> GetAsync(string edgeId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT edge_id, host_base_url, hostname, version, last_seen_at, last_error,
                   acquisition_status::text, delivery_status::text
            FROM platform_edges WHERE edge_id = $1
            """;
        command.Parameters.AddWithValue(edgeId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadEdge(reader) : null;
    }

    private static EdgeState ReadEdge(NpgsqlDataReader reader)
        => new(reader.GetString(0))
        {
            HostBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
            Hostname = reader.IsDBNull(2) ? null : reader.GetString(2),
            Version = reader.IsDBNull(3) ? null : reader.GetString(3),
            LastSeen = reader.GetFieldValue<DateTimeOffset>(4),
            LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
            Acquisition = Deserialize<EdgeAcquisitionRuntimeStatus>(reader, 6),
            Delivery = Deserialize<EdgeDeliveryRuntimeStatus>(reader, 7)
        };

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
                  edge_id, recorded_at, acquisition_state, last_valid_snapshot_at,
                  valid_snapshot_count, emitted_event_count, acquisition_error,
                  delivery_state, pending_event_count, oldest_pending_event_at,
                  backlog_capacity_used_percent, shipment_rate_per_second, delivery_error)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
                ON CONFLICT(edge_id, recorded_at) DO UPDATE SET
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
                  delivery_error = EXCLUDED.delivery_error
                """;
            command.Parameters.AddWithValue(edgeId);
            command.Parameters.AddWithValue(receivedAt);
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

    public sealed class EdgeState(string edgeId)
    {
        public string EdgeId { get; } = edgeId;
        public string? HostBaseUrl { get; set; }
        public string? Hostname { get; set; }
        public string? Version { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public string? LastError { get; set; }
        public EdgeAcquisitionRuntimeStatus? Acquisition { get; set; }
        public EdgeDeliveryRuntimeStatus? Delivery { get; set; }
    }
}
