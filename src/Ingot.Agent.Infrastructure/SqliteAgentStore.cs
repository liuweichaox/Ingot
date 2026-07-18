using System.Text.Json;
using Ingot.Contracts.Agents;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Ingot.Agent.Infrastructure;

/// <summary>
/// Local-first Agent control-plane store. WAL keeps readers independent from the single writer and
/// the database remains one portable file. It stores platform artifacts as records, never as
/// model-selected filesystem paths.
/// </summary>
public sealed class SqliteAgentStore : IAgentRunStore, IAgentArtifactStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteAgentStore(IConfiguration configuration)
    {
        var configured = configuration["Agent:DatabasePath"];
        var path = string.IsNullOrWhiteSpace(configured) ? "data/agent.db" : configured.Trim();
        if (!Path.IsPathRooted(path))
            path = Path.Combine(AppContext.BaseDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 15
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;
        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=15000;

                CREATE TABLE IF NOT EXISTS agent_runs (
                  run_id TEXT PRIMARY KEY,
                  actor_id TEXT NOT NULL,
                  status TEXT NOT NULL,
                  created_at TEXT NOT NULL,
                  completed_at TEXT,
                  snapshot TEXT NOT NULL,
                  updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_agent_runs_actor_created
                  ON agent_runs(actor_id, created_at DESC);

                CREATE TABLE IF NOT EXISTS agent_stream_events (
                  sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                  run_id TEXT NOT NULL REFERENCES agent_runs(run_id) ON DELETE CASCADE,
                  event_type TEXT NOT NULL,
                  occurred_at TEXT NOT NULL,
                  data TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_agent_stream_events_run_sequence
                  ON agent_stream_events(run_id, sequence);

                CREATE TABLE IF NOT EXISTS agent_artifacts (
                  artifact_id TEXT PRIMARY KEY,
                  actor_id TEXT NOT NULL,
                  run_id TEXT,
                  kind TEXT NOT NULL,
                  title TEXT NOT NULL,
                  format TEXT NOT NULL,
                  content TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  metadata TEXT,
                  created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_agent_artifacts_actor_created
                  ON agent_artifacts(actor_id, created_at DESC);
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await RecoverInterruptedRunsAsync(connection, ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO agent_runs(run_id, actor_id, status, created_at, completed_at, snapshot, updated_at)
            VALUES ($runId, $actorId, $status, $createdAt, $completedAt, $snapshot, $updatedAt);
            """;
        BindRun(command, run);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM agent_runs WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : JsonSerializer.Deserialize<AgentRunSnapshot>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
        string surface,
        string actorId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT snapshot FROM agent_runs
            WHERE actor_id = $actorId
              AND json_extract(snapshot, '$.surface') = $surface
              AND ($before IS NULL OR created_at < $before)
            ORDER BY created_at DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$actorId", actorId);
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$before", before?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 101));
        var result = new List<AgentRunSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var run = JsonSerializer.Deserialize<AgentRunSnapshot>(reader.GetString(0), JsonOptions);
            if (run is not null)
                result.Add(run);
        }
        return result;
    }

    public async Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE agent_runs SET actor_id=$actorId, status=$status, completed_at=$completedAt,
              snapshot=$snapshot, updated_at=$updatedAt WHERE run_id=$runId;
            """;
        BindRun(command, run);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Agent 运行不存在: {run.RunId}");
    }

    public async Task<AgentStreamEvent> AppendEventAsync(
        string runId,
        string type,
        object? data,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var occurredAt = DateTimeOffset.UtcNow;
        var element = data is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(data, JsonOptions);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO agent_stream_events(run_id,event_type,occurred_at,data)
            VALUES ($runId,$type,$occurredAt,$data);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$occurredAt", occurredAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$data", element?.GetRawText() ?? (object)DBNull.Value);
        var sequence = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        return new AgentStreamEvent { Sequence = sequence, Type = type, OccurredAt = occurredAt, Data = element };
    }

    public async Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sequence,event_type,occurred_at,data FROM agent_stream_events
            WHERE run_id=$runId AND sequence>$after ORDER BY sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$after", Math.Max(0, afterSequence));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var result = new List<AgentStreamEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new AgentStreamEvent
            {
                Sequence = reader.GetInt64(0),
                Type = reader.GetString(1),
                OccurredAt = DateTimeOffset.Parse(reader.GetString(2)),
                Data = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(3), JsonOptions)
            });
        }
        return result;
    }

    public async Task<AgentArtifact> SaveAsync(
        string actorId,
        string? runId,
        string kind,
        string title,
        string format,
        string content,
        JsonElement? metadata,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (!AgentArtifactKinds.All.Contains(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "不支持的 Agent 制品类型。");
        if (content.Length > 1_000_000)
            throw new ArgumentException("单个 Agent 制品不得超过 1 MB。", nameof(content));
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText =
            "SELECT COALESCE(MAX(version), 0) + 1 FROM agent_artifacts WHERE actor_id=$actor AND kind=$kind AND title=$title;";
        versionCommand.Parameters.AddWithValue("$actor", actorId);
        versionCommand.Parameters.AddWithValue("$kind", kind);
        versionCommand.Parameters.AddWithValue("$title", title.Trim());
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
        var artifact = new AgentArtifact
        {
            ArtifactId = Guid.CreateVersion7().ToString(),
            ActorId = actorId,
            RunId = runId,
            Kind = kind,
            Title = title.Trim(),
            Format = format.Trim(),
            Content = content,
            Version = version,
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO agent_artifacts(artifact_id,actor_id,run_id,kind,title,format,content,version,metadata,created_at)
            VALUES($id,$actor,$run,$kind,$title,$format,$content,$version,$metadata,$created);
            """;
        BindArtifact(command, artifact);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return artifact;
    }

    public async Task<AgentArtifact?> GetAsync(string actorId, string artifactId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT artifact_id,actor_id,run_id,kind,title,format,content,version,metadata,created_at FROM agent_artifacts WHERE actor_id=$actor AND artifact_id=$id;";
        command.Parameters.AddWithValue("$actor", actorId);
        command.Parameters.AddWithValue("$id", artifactId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadArtifact(reader) : null;
    }

    public async Task<IReadOnlyList<AgentArtifact>> ListAsync(string actorId, int limit, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT artifact_id,actor_id,run_id,kind,title,format,content,version,metadata,created_at FROM agent_artifacts WHERE actor_id=$actor ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$actor", actorId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        var result = new List<AgentArtifact>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadArtifact(reader));
        return result;
    }

    public void Dispose() => _initializeLock.Dispose();

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static async Task RecoverInterruptedRunsAsync(SqliteConnection connection, CancellationToken ct)
    {
        var interrupted = new List<AgentRunSnapshot>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText =
                "SELECT snapshot FROM agent_runs WHERE status IN ('queued','running','cancelling');";
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var run = JsonSerializer.Deserialize<AgentRunSnapshot>(reader.GetString(0), JsonOptions);
                if (run is not null)
                    interrupted.Add(run);
            }
        }

        foreach (var run in interrupted)
        {
            var recovered = run with
            {
                Status = AgentRunStatuses.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = "服务重启，未完成的运行已终止。"
            };
            await using var transaction = connection.BeginTransaction();
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    "UPDATE agent_runs SET status=$status, completed_at=$completedAt, snapshot=$snapshot, updated_at=$updatedAt WHERE run_id=$runId;";
                BindRun(update, recovered);
                await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await using (var streamEvent = connection.CreateCommand())
            {
                streamEvent.Transaction = transaction;
                streamEvent.CommandText =
                    "INSERT INTO agent_stream_events(run_id,event_type,occurred_at,data) VALUES($runId,$type,$occurredAt,$data);";
                streamEvent.Parameters.AddWithValue("$runId", recovered.RunId);
                streamEvent.Parameters.AddWithValue("$type", AgentStreamEventTypes.RunFailed);
                streamEvent.Parameters.AddWithValue("$occurredAt", recovered.CompletedAt!.Value.UtcDateTime.ToString("O"));
                streamEvent.Parameters.AddWithValue(
                    "$data",
                    JsonSerializer.Serialize(new { error = recovered.Error }, JsonOptions));
                await streamEvent.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private static void BindRun(SqliteCommand command, AgentRunSnapshot run)
    {
        command.Parameters.AddWithValue("$runId", run.RunId);
        command.Parameters.AddWithValue("$actorId", run.ActorId);
        command.Parameters.AddWithValue("$status", run.Status);
        command.Parameters.AddWithValue("$createdAt", run.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", run.CompletedAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(run, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
    }

    private static void BindArtifact(SqliteCommand command, AgentArtifact artifact)
    {
        command.Parameters.AddWithValue("$id", artifact.ArtifactId);
        command.Parameters.AddWithValue("$actor", artifact.ActorId);
        command.Parameters.AddWithValue("$run", artifact.RunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$kind", artifact.Kind);
        command.Parameters.AddWithValue("$title", artifact.Title);
        command.Parameters.AddWithValue("$format", artifact.Format);
        command.Parameters.AddWithValue("$content", artifact.Content);
        command.Parameters.AddWithValue("$version", artifact.Version);
        command.Parameters.AddWithValue("$metadata", artifact.Metadata?.GetRawText() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", artifact.CreatedAt.UtcDateTime.ToString("O"));
    }

    private static AgentArtifact ReadArtifact(SqliteDataReader reader) => new()
    {
        ArtifactId = reader.GetString(0),
        ActorId = reader.GetString(1),
        RunId = reader.IsDBNull(2) ? null : reader.GetString(2),
        Kind = reader.GetString(3),
        Title = reader.GetString(4),
        Format = reader.GetString(5),
        Content = reader.GetString(6),
        Version = reader.GetInt32(7),
        Metadata = reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(8), JsonOptions),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(9))
    };
}
