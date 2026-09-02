// 使用 PostgreSQL 保存 Agent 快照、事件流和带租约的持久运行队列。
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.AgentRuns;

public sealed class PostgresAgentRunStore(NpgsqlDataSource dataSource) : IDurableAgentRunStore
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
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var conversation = new NpgsqlCommand(
                         """
                         INSERT INTO chat_conversations(
                           conversation_id, user_id, title, page_context, status,
                           created_at, updated_at, last_message_at, version)
                         VALUES ($1, $2, $3, $4, 'active', $5, $5, $5, 1)
                         ON CONFLICT (conversation_id) DO NOTHING
                         """, connection, transaction))
        {
            conversation.Parameters.AddWithValue(ConversationId(run));
            conversation.Parameters.AddWithValue(run.UserId);
            conversation.Parameters.AddWithValue(
                run.Question.Length <= 200 ? run.Question : run.Question[..200]);
            conversation.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = run.PageContext is null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(run.PageContext, JsonOptions)
            });
            conversation.Parameters.AddWithValue(run.CreatedAt);
            await conversation.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO agent_runs(
              run_id, user_id, entry_point, status, created_at, completed_at, snapshot, updated_at,
              conversation_id, trigger_message_id, response_message_id)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now(), $8, $9, $10)
            """, connection, transaction);
        BindRun(command, run);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
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

    public async Task<ClaimedAgentRun?> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH candidate AS (
              SELECT run_id
              FROM agent_runs
              WHERE status = 'queued'
                 OR (status IN ('running', 'cancelling') AND lease_expires_at < now())
              ORDER BY created_at, run_id
              FOR UPDATE SKIP LOCKED
              LIMIT 1
            )
            UPDATE agent_runs run
            SET status = CASE WHEN run.status = 'cancelling' THEN 'cancelling' ELSE 'running' END,
                snapshot = jsonb_set(
                  run.snapshot,
                  '{status}',
                  to_jsonb(CASE WHEN run.status = 'cancelling' THEN 'cancelling'::text ELSE 'running'::text END),
                  true),
                lease_owner = $1,
                lease_expires_at = now() + $2,
                lease_generation = run.lease_generation + 1,
                attempt_count = attempt_count + 1,
                updated_at = now()
            FROM candidate
            WHERE run.run_id = candidate.run_id
            RETURNING run.snapshot::text, run.lease_generation
            """);
        command.Parameters.AddWithValue(leaseOwner);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Interval,
            Value = leaseDuration
        });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var run = JsonSerializer.Deserialize<AgentRunSnapshot>(reader.GetString(0), JsonOptions)
                  ?? throw new InvalidOperationException("Agent 运行快照无效。");
        return new ClaimedAgentRun(run, new AgentRunLease(run.RunId, leaseOwner, reader.GetInt64(1)));
    }

    public async Task<bool> RenewLeaseAsync(
        AgentRunLease lease,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agent_runs
            SET lease_expires_at = now() + $4, updated_at = now()
            WHERE run_id = $1 AND lease_owner = $2 AND lease_generation = $3
              AND status IN ('running', 'cancelling')
            """);
        command.Parameters.AddWithValue(lease.RunId);
        command.Parameters.AddWithValue(lease.Owner);
        command.Parameters.AddWithValue(lease.Generation);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Interval,
            Value = leaseDuration
        });
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task ReleaseLeaseAsync(
        AgentRunLease lease,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agent_runs
            SET lease_owner = NULL, lease_expires_at = NULL, updated_at = now()
            WHERE run_id = $1 AND lease_owner = $2 AND lease_generation = $3
              AND status IN ('completed', 'cancelled', 'failed')
            """);
        command.Parameters.AddWithValue(lease.RunId);
        command.Parameters.AddWithValue(lease.Owner);
        command.Parameters.AddWithValue(lease.Generation);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> UpdateLeasedAsync(
        AgentRunSnapshot run,
        AgentRunLease lease,
        CancellationToken ct = default)
    {
        if (!string.Equals(run.RunId, lease.RunId, StringComparison.Ordinal))
            throw new ArgumentException("租约和运行编号不匹配。", nameof(lease));
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agent_runs
            SET user_id = $2, entry_point = $3, status = $4, created_at = $5,
                completed_at = $6, snapshot = $7, updated_at = now(),
                conversation_id = $8, trigger_message_id = $9, response_message_id = $10
            WHERE run_id = $1 AND lease_owner = $11 AND lease_generation = $12
              AND (status = 'running' OR (status = 'cancelling' AND $4 = 'cancelled'))
            """);
        BindRun(command, run);
        command.Parameters.AddWithValue(lease.Owner);
        command.Parameters.AddWithValue(lease.Generation);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
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

    public async Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        int limit,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT snapshot::text
            FROM agent_runs
            WHERE user_id = $1
              AND entry_point = $2
              AND conversation_id = $3::uuid
            ORDER BY created_at, run_id
            LIMIT $4
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(entryPoint);
        command.Parameters.AddWithValue(conversationId);
        command.Parameters.AddWithValue(Math.Clamp(limit, 1, 100));
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
                completed_at = $6, snapshot = $7, updated_at = now(),
                conversation_id = $8, trigger_message_id = $9, response_message_id = $10
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
        await using var command = new NpgsqlCommand(
            "DELETE FROM agent_runs WHERE run_id = $1;", connection, transaction);
        command.Parameters.AddWithValue(runId);
        var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<bool> DeleteConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM agent_runs
            WHERE user_id = $1
              AND entry_point = $2
              AND conversation_id = $3::uuid
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(entryPoint);
        command.Parameters.AddWithValue(conversationId);
        var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
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

    public async Task<AgentStreamEvent?> AppendLeasedEventAsync(
        string runId,
        AgentRunLease lease,
        string type,
        object? data,
        CancellationToken ct = default)
    {
        if (!string.Equals(runId, lease.RunId, StringComparison.Ordinal))
            throw new ArgumentException("租约和运行编号不匹配。", nameof(lease));
        var occurredAt = DateTimeOffset.UtcNow;
        var element = data is null
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(data, JsonOptions);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO agent_stream_events(run_id, event_type, occurred_at, data)
            SELECT $1, $2, $3, $4
            WHERE EXISTS (
              SELECT 1 FROM agent_runs
              WHERE run_id = $1 AND lease_owner = $5 AND lease_generation = $6
            )
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
        command.Parameters.AddWithValue(lease.Owner);
        command.Parameters.AddWithValue(lease.Generation);
        var sequence = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return sequence is null or DBNull
            ? null
            : new AgentStreamEvent
            {
                Sequence = Convert.ToInt64(sequence),
                Type = type,
                OccurredAt = occurredAt,
                Data = element
            };
    }

    public async Task<AgentRunSnapshot?> RequestCancellationAsync(
        string runId,
        string userId,
        string reason,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agent_runs
            SET status = CASE WHEN status = 'queued' THEN 'cancelled' ELSE 'cancelling' END,
                completed_at = CASE WHEN status = 'queued' THEN now() ELSE completed_at END,
                snapshot = jsonb_set(
                  jsonb_set(snapshot, '{status}',
                    to_jsonb(CASE WHEN status = 'queued' THEN 'cancelled'::text ELSE 'cancelling'::text END), true),
                  '{cancellationReason}', to_jsonb($3::text), true),
                updated_at = now()
            WHERE run_id = $1 AND user_id = $2 AND status IN ('queued', 'running')
            RETURNING snapshot::text
            """);
        command.Parameters.AddWithValue(runId);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(reason);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : JsonSerializer.Deserialize<AgentRunSnapshot>((string)value, JsonOptions);
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
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = ConversationId(run)
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = Guid.TryParse(run.TriggerMessageId, out var triggerMessageId)
                ? triggerMessageId
                : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = Guid.TryParse(run.ResponseMessageId, out var responseMessageId)
                ? responseMessageId
                : DBNull.Value
        });
    }

    private static Guid ConversationId(AgentRunSnapshot run)
        => Guid.TryParse(run.ConversationId ?? run.RunId, out var conversationId)
            ? conversationId
            : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(run.ConversationId ?? run.RunId)));
}
