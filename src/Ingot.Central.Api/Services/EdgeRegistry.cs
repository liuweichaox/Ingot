using Microsoft.Data.Sqlite;

namespace Ingot.Central.Api.Services;

public sealed class EdgeRegistry
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    private const string ColumnAgentBaseUrl = "agent_base_url";

    public EdgeRegistry(IConfiguration configuration)
    {
        _dbPath = configuration["Central:DatabasePath"] ?? "Data/central.db";
        if (!Path.IsPathRooted(_dbPath)) _dbPath = Path.Combine(AppContext.BaseDirectory, _dbPath);

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureSchema();
    }

    public IReadOnlyCollection<EdgeState> List()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT edge_id, agent_base_url, hostname, version, last_seen_utc, last_error
                          FROM edges
                          ORDER BY last_seen_utc DESC;
                          """;

        using var reader = cmd.ExecuteReader();
        var list = new List<EdgeState>();
        while (reader.Read())
        {
            list.Add(new EdgeState(reader.GetString(0))
            {
                AgentBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
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

    public EdgeState Upsert(string edgeId, string? agentBaseUrl, string? hostname, string? version, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO edges(edge_id, agent_base_url, hostname, version, last_seen_utc, last_error)
                          VALUES ($edge_id, $agent_base_url, $hostname, $version, $last_seen_utc, NULL)
                          ON CONFLICT(edge_id) DO UPDATE SET
                            agent_base_url = COALESCE(excluded.agent_base_url, edges.agent_base_url),
                            hostname      = COALESCE(excluded.hostname, edges.hostname),
                            version       = COALESCE(excluded.version, edges.version),
                            last_seen_utc = excluded.last_seen_utc;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);
        cmd.Parameters.AddWithValue("$agent_base_url", (object?)NormalizeBaseUrl(agentBaseUrl) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hostname", (object?)hostname ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_seen_utc", now.ToString("O"));
        cmd.ExecuteNonQuery();

        return Get(edgeId) ?? new EdgeState(edgeId)
        {
            AgentBaseUrl = agentBaseUrl,
            Hostname = hostname,
            Version = version,
            LastSeen = now
        };
    }

    public EdgeState Heartbeat(string edgeId, string? agentBaseUrl, string? lastError, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO edges(edge_id, agent_base_url, hostname, version, last_seen_utc, last_error)
                          VALUES ($edge_id, $agent_base_url, NULL, NULL, $last_seen_utc, $last_error)
                          ON CONFLICT(edge_id) DO UPDATE SET
                            last_seen_utc  = excluded.last_seen_utc,
                            agent_base_url = COALESCE(excluded.agent_base_url, edges.agent_base_url),
                            last_error     = excluded.last_error;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);
        cmd.Parameters.AddWithValue("$agent_base_url", (object?)NormalizeBaseUrl(agentBaseUrl) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_seen_utc", now.ToString("O"));
        cmd.Parameters.AddWithValue("$last_error", (object?)lastError ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Get(edgeId) ?? new EdgeState(edgeId)
        {
            AgentBaseUrl = agentBaseUrl,
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
        var columns = GetColumns(conn);

        if (columns.Count == 0)
        {
            CreateTable(conn);
            return;
        }

        if (columns.Contains("workshop_id") || columns.Contains("buffer_backlog"))
        {
            RebuildTable(conn, columns);
            columns = GetColumns(conn);
        }

        EnsureColumn(conn, columns, ColumnAgentBaseUrl, "TEXT NULL");
        EnsureColumn(conn, columns, "hostname", "TEXT NULL");
        EnsureColumn(conn, columns, "version", "TEXT NULL");
        EnsureColumn(conn, columns, "last_error", "TEXT NULL");
    }

    private EdgeState? Get(string edgeId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT edge_id, agent_base_url, hostname, version, last_seen_utc, last_error
                          FROM edges
                          WHERE edge_id = $edge_id;
                          """;
        cmd.Parameters.AddWithValue("$edge_id", edgeId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new EdgeState(reader.GetString(0))
        {
            AgentBaseUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
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
        public string? AgentBaseUrl { get; set; }
        public string? Hostname { get; set; }
        public string? Version { get; set; }
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public string? LastError { get; set; }
    }

    private static HashSet<string> GetColumns(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(edges);";
        using var reader = cmd.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var name = reader.GetString(1);
            columns.Add(name);
        }

        return columns;
    }

    private static void CreateTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS edges (
                            edge_id        TEXT PRIMARY KEY,
                            agent_base_url TEXT NULL,
                            hostname       TEXT NULL,
                            version        TEXT NULL,
                            last_seen_utc  TEXT NOT NULL,
                            last_error     TEXT NULL
                          );
                          """;
        cmd.ExecuteNonQuery();
    }

    private static void RebuildTable(SqliteConnection conn, HashSet<string> columns)
    {
        var agentBaseUrl = columns.Contains(ColumnAgentBaseUrl) ? ColumnAgentBaseUrl : "NULL";
        var hostname = columns.Contains("hostname") ? "hostname" : "NULL";
        var version = columns.Contains("version") ? "version" : "NULL";
        var lastSeen = columns.Contains("last_seen_utc") ? "last_seen_utc" : "CURRENT_TIMESTAMP";
        var lastError = columns.Contains("last_error") ? "last_error" : "NULL";

        using var rebuild = conn.CreateCommand();
        rebuild.CommandText = $"""
                               CREATE TABLE edges_v2 (
                                 edge_id        TEXT PRIMARY KEY,
                                 agent_base_url TEXT NULL,
                                 hostname       TEXT NULL,
                                 version        TEXT NULL,
                                 last_seen_utc  TEXT NOT NULL,
                                 last_error     TEXT NULL
                               );
                               INSERT OR REPLACE INTO edges_v2(edge_id, agent_base_url, hostname, version, last_seen_utc, last_error)
                               SELECT edge_id, {agentBaseUrl}, {hostname}, {version}, {lastSeen}, {lastError}
                               FROM edges;
                               DROP TABLE edges;
                               ALTER TABLE edges_v2 RENAME TO edges;
                               """;
        rebuild.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection conn, HashSet<string> columns, string columnName, string columnDefinition)
    {
        if (columns.Contains(columnName)) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE edges ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static string? NormalizeBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return trimmed.EndsWith("/") ? trimmed.TrimEnd('/') : trimmed;
    }
}
