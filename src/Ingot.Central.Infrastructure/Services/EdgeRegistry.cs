using Microsoft.Data.Sqlite;

namespace Ingot.Central.Infrastructure.Services;

public sealed class EdgeRegistry
{
    private readonly string _connectionString;

    public EdgeRegistry(IConfiguration configuration)
    {
        var databasePath = configuration["Central:DatabasePath"] ?? "Data/central.db";
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
                          SELECT edge_id, host_base_url, hostname, version, last_seen_utc, last_error
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
                LastError = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return list;
    }

    public EdgeState? Find(string edgeId)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return null;
        return Get(edgeId);
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

    public EdgeState Heartbeat(string edgeId, string? hostBaseUrl, string? lastError, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO edges(edge_id, host_base_url, hostname, version, last_seen_utc, last_error)
                          VALUES ($edge_id, $host_base_url, NULL, NULL, $last_seen_utc, $last_error)
                          ON CONFLICT(edge_id) DO UPDATE SET
                            last_seen_utc  = excluded.last_seen_utc,
                            host_base_url = COALESCE(excluded.host_base_url, edges.host_base_url),
                            last_error     = excluded.last_error;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);
        cmd.Parameters.AddWithValue("$host_base_url", (object?)NormalizeBaseUrl(hostBaseUrl) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_seen_utc", now.ToString("O"));
        cmd.Parameters.AddWithValue("$last_error", (object?)lastError ?? DBNull.Value);
        cmd.ExecuteNonQuery();

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
                          SELECT edge_id, host_base_url, hostname, version, last_seen_utc, last_error
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
            LastError = reader.IsDBNull(5) ? null : reader.GetString(5)
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
                            last_error     TEXT NULL
                          );
                          """;
        cmd.ExecuteNonQuery();
    }

    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return trimmed.EndsWith("/") ? trimmed.TrimEnd('/') : trimmed;
    }
}
