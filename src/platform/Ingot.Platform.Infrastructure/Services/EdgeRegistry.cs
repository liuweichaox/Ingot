using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Microsoft.Data.Sqlite;

namespace Ingot.Platform.Infrastructure.Services;

public sealed class EdgeRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public EdgeRegistry(IConfiguration configuration)
    {
        var databasePath = configuration["Platform:DatabasePath"]
            ?? configuration["Central:DatabasePath"]
            ?? "Data/platform.db";
        if (!Path.IsPathRooted(databasePath))
            databasePath = Path.Combine(AppContext.BaseDirectory, databasePath);

        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureSchema();
    }

    public IReadOnlyCollection<EdgeState> List()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT edge_id, host_base_url, hostname, version, last_seen_utc, last_error,
                                 acquisition_status_json, delivery_status_json
                          FROM edges
                          ORDER BY last_seen_utc DESC;
                          """;

        using var reader = cmd.ExecuteReader();
        var list = new List<EdgeState>();
        while (reader.Read())
        {
            list.Add(new EdgeState(reader.GetString(0))
            {
                HostBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
                Hostname = reader.IsDBNull(2) ? null : reader.GetString(2),
                Version = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastSeen = ParseStoredTimestamp(reader.IsDBNull(4) ? null : reader.GetString(4)),
                LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
                Acquisition = DeserializeAcquisitionStatus(reader.IsDBNull(6) ? null : reader.GetString(6)),
                Delivery = DeserializeDeliveryStatus(reader.IsDBNull(7) ? null : reader.GetString(7))
            });
        }

        return list;
    }

    public EdgeState? Find(string edgeId)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return null;
        return Get(edgeId);
    }

    public IReadOnlyList<EdgeRuntimeStatusHistoryItem> ListStatusHistory(string edgeId, int limit = 288)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return [];
        limit = Math.Clamp(limit, 1, 1000);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT recorded_at_utc, acquisition_state, last_valid_snapshot_at,
                                 valid_snapshot_count, emitted_event_count, acquisition_error,
                                 delivery_state, pending_event_count, oldest_pending_event_at,
                                 backlog_capacity_used_percent, shipment_rate_per_second, delivery_error
                          FROM edge_runtime_status_history
                          WHERE edge_id = $edge_id
                          ORDER BY recorded_at_utc DESC
                          LIMIT $limit;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId.Trim());
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var values = new List<EdgeRuntimeStatusHistoryItem>();
        while (reader.Read())
        {
            values.Add(new EdgeRuntimeStatusHistoryItem
            {
                EdgeId = edgeId.Trim(),
                RecordedAt = ParseStoredTimestamp(reader.GetString(0)),
                AcquisitionState = reader.IsDBNull(1) ? null : reader.GetString(1),
                LastValidSnapshotAt = reader.IsDBNull(2) ? null : ParseStoredTimestamp(reader.GetString(2)),
                ValidSnapshotCount = reader.GetInt64(3),
                EmittedEventCount = reader.GetInt64(4),
                AcquisitionError = reader.IsDBNull(5) ? null : reader.GetString(5),
                DeliveryState = reader.IsDBNull(6) ? null : reader.GetString(6),
                PendingEventCount = reader.GetInt64(7),
                OldestPendingEventAt = reader.IsDBNull(8) ? null : ParseStoredTimestamp(reader.GetString(8)),
                BacklogCapacityUsedPercent = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                ShipmentRatePerSecond = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                DeliveryError = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        }
        return values;
    }

    public EdgeState Upsert(string edgeId, string? hostBaseUrl, string? hostname, string? version, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO edges(edge_id, host_base_url, hostname, version, last_seen_utc, last_error)
                          VALUES ($edge_id, $host_base_url, $hostname, $version, $last_seen_utc, NULL)
                          ON CONFLICT(edge_id) DO UPDATE SET
                            host_base_url = COALESCE(excluded.host_base_url, edges.host_base_url),
                            hostname      = COALESCE(excluded.hostname, edges.hostname),
                            version       = COALESCE(excluded.version, edges.version),
                            last_seen_utc = excluded.last_seen_utc;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);
        cmd.Parameters.AddWithValue("$host_base_url", (object?)NormalizeBaseUrl(hostBaseUrl) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hostname", (object?)hostname ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_seen_utc", now.ToString("O"));
        cmd.ExecuteNonQuery();

        return Get(edgeId) ?? new EdgeState(edgeId)
        {
            HostBaseUrl = hostBaseUrl,
            Hostname = hostname,
            Version = version,
            LastSeen = now
        };
    }

    public EdgeState Heartbeat(
        string edgeId,
        string? hostBaseUrl,
        string? lastError,
        EdgeAcquisitionRuntimeStatus? acquisition,
        DateTimeOffset now,
        EdgeDeliveryRuntimeStatus? delivery = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO edges(
                            edge_id, host_base_url, hostname, version, last_seen_utc, last_error,
                            acquisition_status_json, delivery_status_json)
                          VALUES (
                            $edge_id, $host_base_url, NULL, NULL, $last_seen_utc, $last_error,
                            $acquisition_status_json, $delivery_status_json)
                          ON CONFLICT(edge_id) DO UPDATE SET
                            last_seen_utc  = excluded.last_seen_utc,
                            host_base_url = COALESCE(excluded.host_base_url, edges.host_base_url),
                            last_error     = excluded.last_error,
                            acquisition_status_json = COALESCE(
                              excluded.acquisition_status_json,
                              edges.acquisition_status_json),
                            delivery_status_json = COALESCE(
                              excluded.delivery_status_json,
                              edges.delivery_status_json);
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);
        cmd.Parameters.AddWithValue("$host_base_url", (object?)NormalizeBaseUrl(hostBaseUrl) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_seen_utc", now.ToString("O"));
        cmd.Parameters.AddWithValue("$last_error", (object?)lastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "$acquisition_status_json",
            acquisition is null ? DBNull.Value : JsonSerializer.Serialize(acquisition, JsonOptions));
        cmd.Parameters.AddWithValue(
            "$delivery_status_json",
            delivery is null ? DBNull.Value : JsonSerializer.Serialize(delivery, JsonOptions));
        cmd.ExecuteNonQuery();

        if (acquisition is not null || delivery is not null)
            SaveStatusHistory(conn, edgeId, acquisition, delivery, now);

        return Get(edgeId) ?? new EdgeState(edgeId)
        {
            HostBaseUrl = hostBaseUrl,
            LastSeen = now,
            LastError = lastError
        };
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        CreateTable(conn);
    }

    private EdgeState? Get(string edgeId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT edge_id, host_base_url, hostname, version, last_seen_utc, last_error,
                                 acquisition_status_json, delivery_status_json
                          FROM edges
                          WHERE edge_id = $edge_id;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new EdgeState(reader.GetString(0))
        {
            HostBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
            Hostname = reader.IsDBNull(2) ? null : reader.GetString(2),
            Version = reader.IsDBNull(3) ? null : reader.GetString(3),
            LastSeen = ParseStoredTimestamp(reader.IsDBNull(4) ? null : reader.GetString(4)),
            LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
            Acquisition = DeserializeAcquisitionStatus(reader.IsDBNull(6) ? null : reader.GetString(6)),
            Delivery = DeserializeDeliveryStatus(reader.IsDBNull(7) ? null : reader.GetString(7))
        };
    }

    private static DateTimeOffset ParseStoredTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DateTimeOffset.UtcNow;
        return DateTimeOffset.Parse(value).ToUniversalTime();
    }

    public sealed class EdgeState
    {
        public EdgeState(string edgeId)
        {
            EdgeId = edgeId;
        }

        public string EdgeId { get; }
        public string? HostBaseUrl { get; set; }
        public string? Hostname { get; set; }
        public string? Version { get; set; }
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public string? LastError { get; set; }
        public EdgeAcquisitionRuntimeStatus? Acquisition { get; set; }
        public EdgeDeliveryRuntimeStatus? Delivery { get; set; }
    }

    private static void CreateTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS edges (
                            edge_id        TEXT PRIMARY KEY,
                            host_base_url  TEXT NULL,
                            hostname       TEXT NULL,
                            version        TEXT NULL,
                            last_seen_utc  TEXT NOT NULL,
                            last_error     TEXT NULL,
                            acquisition_status_json TEXT NULL,
                            delivery_status_json TEXT NULL
                          );
                          CREATE TABLE IF NOT EXISTS edge_runtime_status_history (
                            edge_id TEXT NOT NULL,
                            recorded_at_utc TEXT NOT NULL,
                            acquisition_state TEXT NULL,
                            last_valid_snapshot_at TEXT NULL,
                            valid_snapshot_count INTEGER NOT NULL,
                            emitted_event_count INTEGER NOT NULL,
                            acquisition_error TEXT NULL,
                            delivery_state TEXT NULL,
                            pending_event_count INTEGER NOT NULL,
                            oldest_pending_event_at TEXT NULL,
                            backlog_capacity_used_percent REAL NULL,
                            shipment_rate_per_second REAL NULL,
                            delivery_error TEXT NULL,
                            PRIMARY KEY(edge_id, recorded_at_utc)
                          );
                          CREATE INDEX IF NOT EXISTS idx_edge_runtime_status_history_time
                            ON edge_runtime_status_history(edge_id, recorded_at_utc DESC);
                          """;
        cmd.ExecuteNonQuery();
        EnsureColumn(conn, "edges", "acquisition_status_json", "TEXT NULL");
        EnsureColumn(conn, "edges", "delivery_status_json", "TEXT NULL");
    }

    private static void SaveStatusHistory(
        SqliteConnection connection,
        string edgeId,
        EdgeAcquisitionRuntimeStatus? acquisition,
        EdgeDeliveryRuntimeStatus? delivery,
        DateTimeOffset receivedAt)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
                             INSERT OR REPLACE INTO edge_runtime_status_history(
                               edge_id, recorded_at_utc, acquisition_state, last_valid_snapshot_at,
                               valid_snapshot_count, emitted_event_count, acquisition_error,
                               delivery_state, pending_event_count, oldest_pending_event_at,
                               backlog_capacity_used_percent, shipment_rate_per_second, delivery_error)
                             VALUES (
                               $edge_id, $recorded_at, $acquisition_state, $last_valid_snapshot_at,
                               $valid_snapshot_count, $emitted_event_count, $acquisition_error,
                               $delivery_state, $pending_event_count, $oldest_pending_event_at,
                               $capacity_percent, $shipment_rate, $delivery_error);
                             """;
        insert.Parameters.AddWithValue("$edge_id", edgeId.Trim());
        insert.Parameters.AddWithValue("$recorded_at", receivedAt.ToUniversalTime().ToString("O"));
        insert.Parameters.AddWithValue("$acquisition_state", (object?)acquisition?.State ?? DBNull.Value);
        insert.Parameters.AddWithValue("$last_valid_snapshot_at", acquisition?.LastValidSnapshotAt is { } lastValid
            ? lastValid.ToUniversalTime().ToString("O")
            : DBNull.Value);
        insert.Parameters.AddWithValue("$valid_snapshot_count", acquisition?.ValidSnapshotCount ?? 0);
        insert.Parameters.AddWithValue("$emitted_event_count", acquisition?.EmittedEventCount ?? 0);
        insert.Parameters.AddWithValue("$acquisition_error", (object?)acquisition?.LastError ?? DBNull.Value);
        insert.Parameters.AddWithValue("$delivery_state", (object?)delivery?.State ?? DBNull.Value);
        insert.Parameters.AddWithValue("$pending_event_count", delivery?.PendingEventCount ?? 0);
        insert.Parameters.AddWithValue("$oldest_pending_event_at", delivery?.OldestPendingEventAt is { } oldest
            ? oldest.ToUniversalTime().ToString("O")
            : DBNull.Value);
        insert.Parameters.AddWithValue("$capacity_percent", (object?)delivery?.BacklogCapacityUsedPercent ?? DBNull.Value);
        insert.Parameters.AddWithValue("$shipment_rate", (object?)delivery?.ShipmentRatePerSecond ?? DBNull.Value);
        insert.Parameters.AddWithValue("$delivery_error", (object?)delivery?.LastError ?? DBNull.Value);
        insert.ExecuteNonQuery();

        using var prune = connection.CreateCommand();
        prune.CommandText = """
                            DELETE FROM edge_runtime_status_history
                            WHERE edge_id = $edge_id AND recorded_at_utc < $cutoff;
                            """;
        prune.Parameters.AddWithValue("$edge_id", edgeId.Trim());
        prune.Parameters.AddWithValue("$cutoff", receivedAt.AddDays(-7).ToUniversalTime().ToString("O"));
        prune.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        using var reader = inspect.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static EdgeAcquisitionRuntimeStatus? DeserializeAcquisitionStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return JsonSerializer.Deserialize<EdgeAcquisitionRuntimeStatus>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EdgeDeliveryRuntimeStatus? DeserializeDeliveryStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return JsonSerializer.Deserialize<EdgeDeliveryRuntimeStatus>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return trimmed.EndsWith("/") ? trimmed.TrimEnd('/') : trimmed;
    }
}
