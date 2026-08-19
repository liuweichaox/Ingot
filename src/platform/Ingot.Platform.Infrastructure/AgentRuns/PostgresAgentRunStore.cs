using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.AgentRuns;

/// <summary>
///     Production Agent run record source. Run snapshots, streaming events, and formal
///     evaluation references share the same PostgreSQL recovery boundary.
/// </summary>
public sealed class PostgresAgentRunStore(NpgsqlDataSource dataSource) : IAgentRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT to_regclass('public.agent_runs') IS NOT NULL;");
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("Agent 运行表尚未迁移。请先运行 Platform Migrator。");
    }

    public async Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO agent_runs(
              run_id, user_id, entry_point, status, created_at, completed_at, snapshot, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            """);
        BindRun(command, run);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("Agent 运行编号已经存在。", exception);
        }
    }

    public async Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT snapshot::text FROM agent_runs WHERE run_id = $1;");
        command.Parameters.AddWithValue(runId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : JsonSerializer.Deserialize<AgentRunSnapshot>((string)value, JsonOptions);
    }

    public async Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
        string entryPoint,
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT snapshot::text
            FROM agent_runs
            WHERE user_id = $1
              AND entry_point = $2
              AND ($3::timestamptz IS NULL OR created_at < $3)
            ORDER BY created_at DESC, run_id
            LIMIT $4
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(entryPoint);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)before ?? DBNull.Value
        });
        command.Parameters.AddWithValue(Math.Clamp(limit, 1, 101));
        var values = new List<AgentRunSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<AgentRunSnapshot>(reader.GetString(0), JsonOptions);
            if (value is not null) values.Add(value);
        }
        return values;
    }

    public async Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agent_runs
            SET user_id = $2, entry_point = $3, status = $4, created_at = $5,
                completed_at = $6, snapshot = $7, updated_at = now()
            WHERE run_id = $1
            """);
        BindRun(command, run);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException($"Chat 运行不存在: {run.RunId}");
    }

    public async Task<bool> DeleteAsync(string runId, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var evidence = new NpgsqlCommand(
                         "SELECT EXISTS(SELECT 1 FROM golden_question_evaluations WHERE agent_run_id = $1);",
                         connection,
                         transaction))
        {
            evidence.Parameters.AddWithValue(runId);
            if (await evidence.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                throw new InvalidOperationException("该 Chat 运行已经进入正式评测证据，不能删除。");
        }
        await using var command = new NpgsqlCommand(
            "DELETE FROM agent_runs WHERE run_id = $1;", connection, transaction);
        command.Parameters.AddWithValue(runId);
        var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<AgentStreamEvent> AppendEventAsync(
        string runId,
        string type,
        object? data,
        CancellationToken ct = default)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var element = data is null
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(data, JsonOptions);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO agent_stream_events(run_id, event_type, occurred_at, data)
            VALUES ($1, $2, $3, $4)
            RETURNING sequence
            """);
        command.Parameters.AddWithValue(runId);
        command.Parameters.AddWithValue(type);
        command.Parameters.AddWithValue(occurredAt);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = element?.GetRawText() ?? (object)DBNull.Value
        });
        var sequence = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        return new AgentStreamEvent
        {
            Sequence = sequence,
            Type = type,
            OccurredAt = occurredAt,
            Data = element
        };
    }

    public async Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT sequence, event_type, occurred_at, data::text
            FROM agent_stream_events
            WHERE run_id = $1 AND sequence > $2
            ORDER BY sequence
            LIMIT $3
            """);
        command.Parameters.AddWithValue(runId);
        command.Parameters.AddWithValue(Math.Max(0, afterSequence));
        command.Parameters.AddWithValue(Math.Clamp(limit, 1, 500));
        var values = new List<AgentStreamEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            values.Add(new AgentStreamEvent
            {
                Sequence = reader.GetInt64(0),
                Type = reader.GetString(1),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(2),
                Data = reader.IsDBNull(3)
                    ? null
                    : JsonSerializer.Deserialize<JsonElement>(reader.GetString(3), JsonOptions)
            });
        }
        return values;
    }

    private static void BindRun(NpgsqlCommand command, AgentRunSnapshot run)
    {
        command.Parameters.AddWithValue(run.RunId);
        command.Parameters.AddWithValue(run.UserId);
        command.Parameters.AddWithValue(run.EntryPoint);
        command.Parameters.AddWithValue(run.Status);
        command.Parameters.AddWithValue(run.CreatedAt);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)run.CompletedAt ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(run, JsonOptions)
        });
    }
}
