using System.Text.Json;
using Ingot.Contracts.Agents;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Ingot.Agent.Providers;

/// <summary>
/// Local-first Chat run store. WAL keeps readers independent from the single writer and the
/// database remains one portable file.
/// </summary>
public sealed class SqliteAgentStore : IAgentRunStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteAgentStore(IConfiguration configuration)
    {
        var configured = configuration["Chat:DatabasePath"];
        var path = string.IsNullOrWhiteSpace(configured) ? "data/chat.db" : configured.Trim();
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
            throw new InvalidOperationException($"Chat 运行不存在: {run.RunId}");
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

}
