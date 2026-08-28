// 使用 PostgreSQL 事务保存正式对话消息，并把 Agent 终态投影回助手消息。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.Chat;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.AgentRuns;

public sealed class PostgresChatConversationStore(NpgsqlDataSource dataSource)
    : IChatConversationStore, IAgentRunLifecycleSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatConversationSummary?> GetAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            $"""
             {SummarySelect}
             WHERE conversation.conversation_id = $1 AND conversation.user_id = $2
             """);
        command.Parameters.AddWithValue(Guid.Parse(conversationId));
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadSummary(reader) : null;
    }

    public async Task<ChatConversationPage> ListAsync(
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        await using var command = dataSource.CreateCommand(
            $"""
             {SummarySelect}
             WHERE conversation.user_id = $1
               AND ($2::timestamptz IS NULL OR conversation.last_message_at < $2)
             ORDER BY conversation.last_message_at DESC, conversation.conversation_id DESC
             LIMIT $3
             """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)before ?? DBNull.Value
        });
        command.Parameters.AddWithValue(normalizedLimit + 1);
        var items = new List<ChatConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            items.Add(ReadSummary(reader));
        var hasMore = items.Count > normalizedLimit;
        var page = items.Take(normalizedLimit).ToArray();
        return new ChatConversationPage
        {
            Items = page,
            NextBefore = hasMore && page.Length > 0 ? page[^1].LastMessageAt : null
        };
    }

    public async Task<ChatConversationDetail?> GetDetailAsync(
        string conversationId,
        string userId,
        long? beforeSequence,
        int limit,
        CancellationToken ct = default)
    {
        var conversation = await GetAsync(conversationId, userId, ct).ConfigureAwait(false);
        if (conversation is null)
            return null;
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        await using var command = dataSource.CreateCommand(
            """
            WITH selected AS (
              SELECT message_id, conversation_id, sequence, role, status, text_content,
                     answer::text, run_id, error, created_at, completed_at
              FROM chat_messages
              WHERE conversation_id = $1
                AND ($2::bigint IS NULL OR sequence < $2)
              ORDER BY sequence DESC
              LIMIT $3
            )
            SELECT * FROM selected ORDER BY sequence
            """);
        command.Parameters.AddWithValue(Guid.Parse(conversationId));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Bigint,
            Value = (object?)beforeSequence ?? DBNull.Value
        });
        command.Parameters.AddWithValue(normalizedLimit + 1);
        var messages = new List<ChatMessageSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            messages.Add(ReadMessage(reader));
        var hasMore = messages.Count > normalizedLimit;
        var page = hasMore ? messages.Skip(1).ToArray() : messages.ToArray();
        return new ChatConversationDetail
        {
            Conversation = conversation,
            Messages = page,
            NextBeforeSequence = hasMore && page.Length > 0 ? page[0].Sequence : null
        };
    }

    public async Task<ChatTurnReservation> CreateConversationWithTurnAsync(
        ChatConversationSummary conversation,
        string userId,
        string clientMessageId,
        string text,
        CancellationToken ct = default)
    {
        var conversationId = Guid.Parse(conversation.ConversationId);
        var userMessageId = Guid.CreateVersion7();
        var assistantMessageId = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var createConversation = new NpgsqlCommand(
                         """
                         INSERT INTO chat_conversations(
                           conversation_id, user_id, title, page_context, status,
                           created_at, updated_at, last_message_at, version)
                         VALUES ($1, $2, $3, $4, 'active', $5, $5, $5, 1)
                         """, connection, transaction))
        {
            createConversation.Parameters.AddWithValue(conversationId);
            createConversation.Parameters.AddWithValue(userId);
            createConversation.Parameters.AddWithValue(conversation.Title);
            createConversation.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = conversation.PageContext is null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(conversation.PageContext, JsonOptions)
            });
            createConversation.Parameters.AddWithValue(conversation.CreatedAt);
            await createConversation.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await InsertTurnAsync(
            connection,
            transaction,
            conversationId,
            userMessageId,
            assistantMessageId,
            Guid.Parse(clientMessageId),
            1,
            text,
            conversation.CreatedAt,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Reservation(conversationId, userMessageId, assistantMessageId);
    }

    public async Task<ChatTurnReservation> AppendTurnAsync(
        string conversationId,
        string userId,
        string clientMessageId,
        string text,
        CancellationToken ct = default)
    {
        var parsedConversationId = Guid.Parse(conversationId);
        var parsedClientMessageId = Guid.Parse(clientMessageId);
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var conversation = new NpgsqlCommand(
                         """
                         SELECT status
                         FROM chat_conversations
                         WHERE conversation_id = $1 AND user_id = $2
                         FOR UPDATE
                         """, connection, transaction))
        {
            conversation.Parameters.AddWithValue(parsedConversationId);
            conversation.Parameters.AddWithValue(userId);
            var status = await conversation.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
                         ?? throw new KeyNotFoundException("对话不存在。");
            if (!string.Equals(status, ChatConversationStatuses.Active, StringComparison.Ordinal))
                throw new InvalidOperationException("已归档的对话不能继续发送消息。");
        }

        var existing = await FindReservationAsync(
            connection, transaction, parsedConversationId, parsedClientMessageId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing with { AlreadyExists = true };
        }

        await using (var pending = new NpgsqlCommand(
                         """
                         SELECT EXISTS(
                           SELECT 1 FROM chat_messages
                           WHERE conversation_id = $1 AND role = 'assistant'
                             AND status IN ('pending', 'generating'))
                         """, connection, transaction))
        {
            pending.Parameters.AddWithValue(parsedConversationId);
            if (await pending.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                throw new InvalidOperationException("当前对话仍在生成回答，请等待完成或先停止。");
        }

        long nextSequence;
        await using (var sequence = new NpgsqlCommand(
                         "SELECT COALESCE(max(sequence), 0) + 1 FROM chat_messages WHERE conversation_id = $1;",
                         connection,
                         transaction))
        {
            sequence.Parameters.AddWithValue(parsedConversationId);
            nextSequence = Convert.ToInt64(await sequence.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        var now = DateTimeOffset.UtcNow;
        var userMessageId = Guid.CreateVersion7();
        var assistantMessageId = Guid.CreateVersion7();
        await InsertTurnAsync(
            connection,
            transaction,
            parsedConversationId,
            userMessageId,
            assistantMessageId,
            parsedClientMessageId,
            nextSequence,
            text,
            now,
            ct).ConfigureAwait(false);
        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE chat_conversations
                         SET updated_at = $2, last_message_at = $2, version = version + 1
                         WHERE conversation_id = $1
                         """, connection, transaction))
        {
            update.Parameters.AddWithValue(parsedConversationId);
            update.Parameters.AddWithValue(now);
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Reservation(parsedConversationId, userMessageId, assistantMessageId);
    }

    public async Task BindRunAsync(
        string assistantMessageId,
        string runId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE chat_messages
            SET run_id = $2,
                status = CASE WHEN status = 'pending' THEN 'generating' ELSE status END
            WHERE message_id = $1 AND role = 'assistant'
            """);
        command.Parameters.AddWithValue(Guid.Parse(assistantMessageId));
        command.Parameters.AddWithValue(runId);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("待生成的助手消息不存在。");
    }

    public async Task CompleteAssistantMessageAsync(
        AgentRunSnapshot run,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(run.ResponseMessageId, out var messageId))
            return;
        var messageStatus = run.Status switch
        {
            AgentRunStatuses.Completed => ChatMessageStatuses.Completed,
            AgentRunStatuses.Cancelled => ChatMessageStatuses.Cancelled,
            AgentRunStatuses.Failed => ChatMessageStatuses.Failed,
            _ => ChatMessageStatuses.Generating
        };
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
                         """
                         UPDATE chat_messages
                         SET run_id = $2, status = $3, text_content = $4, answer = $5,
                             error = $6, completed_at = $7
                         WHERE message_id = $1 AND role = 'assistant'
                         RETURNING conversation_id
                         """, connection, transaction))
        {
            command.Parameters.AddWithValue(messageId);
            command.Parameters.AddWithValue(run.RunId);
            command.Parameters.AddWithValue(messageStatus);
            command.Parameters.AddWithValue((object?)run.Answer?.Summary ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = run.Answer is null ? DBNull.Value : JsonSerializer.Serialize(run.Answer, JsonOptions)
            });
            command.Parameters.AddWithValue((object?)(run.Error ?? run.CancellationReason) ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.TimestampTz,
                Value = (object?)run.CompletedAt ?? DateTimeOffset.UtcNow
            });
            var conversationId = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (conversationId is Guid parsedConversationId)
            {
                await using var update = new NpgsqlCommand(
                    """
                    UPDATE chat_conversations
                    SET updated_at = $2, last_message_at = $2, version = version + 1
                    WHERE conversation_id = $1
                    """, connection, transaction);
                update.Parameters.AddWithValue(parsedConversationId);
                update.Parameters.AddWithValue(run.CompletedAt ?? DateTimeOffset.UtcNow);
                await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public Task OnTerminalAsync(AgentRunSnapshot run, CancellationToken ct = default)
        => CompleteAssistantMessageAsync(run, ct);

    public async Task FailAssistantMessageAsync(
        string assistantMessageId,
        string error,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE chat_messages
            SET status = 'failed', error = $2, completed_at = now()
            WHERE message_id = $1 AND role = 'assistant' AND status IN ('pending', 'generating')
            """);
        command.Parameters.AddWithValue(Guid.Parse(assistantMessageId));
        command.Parameters.AddWithValue(error);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM chat_conversations WHERE conversation_id = $1 AND user_id = $2;");
        command.Parameters.AddWithValue(Guid.Parse(conversationId));
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private static async Task InsertTurnAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid conversationId,
        Guid userMessageId,
        Guid assistantMessageId,
        Guid clientMessageId,
        long userSequence,
        string text,
        DateTimeOffset createdAt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO chat_messages(
              message_id, conversation_id, sequence, role, status, text_content,
              client_message_id, created_at, completed_at)
            VALUES
              ($1, $3, $5, 'user', 'completed', $7, $4, $8, $8),
              ($2, $3, $6, 'assistant', 'pending', NULL, NULL, $8, NULL)
            """, connection, transaction);
        command.Parameters.AddWithValue(userMessageId);
        command.Parameters.AddWithValue(assistantMessageId);
        command.Parameters.AddWithValue(conversationId);
        command.Parameters.AddWithValue(clientMessageId);
        command.Parameters.AddWithValue(userSequence);
        command.Parameters.AddWithValue(userSequence + 1);
        command.Parameters.AddWithValue(text);
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<ChatTurnReservation?> FindReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid conversationId,
        Guid clientMessageId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT user_message.message_id,
                   assistant_message.message_id,
                   assistant_message.run_id
            FROM chat_messages user_message
            JOIN chat_messages assistant_message
              ON assistant_message.conversation_id = user_message.conversation_id
             AND assistant_message.sequence = user_message.sequence + 1
             AND assistant_message.role = 'assistant'
            WHERE user_message.conversation_id = $1
              AND user_message.client_message_id = $2
              AND user_message.role = 'user'
            """, connection, transaction);
        command.Parameters.AddWithValue(conversationId);
        command.Parameters.AddWithValue(clientMessageId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new ChatTurnReservation
        {
            ConversationId = conversationId.ToString(),
            UserMessageId = reader.GetGuid(0).ToString(),
            AssistantMessageId = reader.GetGuid(1).ToString(),
            RunId = reader.IsDBNull(2) ? null : reader.GetString(2),
            AlreadyExists = true
        };
    }

    private static ChatTurnReservation Reservation(
        Guid conversationId,
        Guid userMessageId,
        Guid assistantMessageId)
        => new()
        {
            ConversationId = conversationId.ToString(),
            UserMessageId = userMessageId.ToString(),
            AssistantMessageId = assistantMessageId.ToString()
        };

    private static ChatConversationSummary ReadSummary(NpgsqlDataReader reader)
        => new()
        {
            ConversationId = reader.GetGuid(0).ToString(),
            Title = reader.GetString(1),
            PageContext = reader.IsDBNull(2)
                ? null
                : JsonSerializer.Deserialize<PageContextRef>(reader.GetString(2), JsonOptions),
            Status = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(5),
            LastMessageAt = reader.GetFieldValue<DateTimeOffset>(6),
            LastMessagePreview = reader.IsDBNull(7) ? null : reader.GetString(7),
            LastMessageStatus = reader.IsDBNull(8) ? null : reader.GetString(8)
        };

    private static ChatMessageSnapshot ReadMessage(NpgsqlDataReader reader)
        => new()
        {
            MessageId = reader.GetGuid(0).ToString(),
            ConversationId = reader.GetGuid(1).ToString(),
            Sequence = reader.GetInt64(2),
            Role = reader.GetString(3),
            Status = reader.GetString(4),
            Text = reader.IsDBNull(5) ? null : reader.GetString(5),
            Answer = reader.IsDBNull(6)
                ? null
                : JsonSerializer.Deserialize<AnalysisAnswer>(reader.GetString(6), JsonOptions),
            RunId = reader.IsDBNull(7) ? null : reader.GetString(7),
            Error = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(9),
            CompletedAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)
        };

    private const string SummarySelect =
        """
        SELECT conversation.conversation_id,
               conversation.title,
               conversation.page_context::text,
               conversation.status,
               conversation.created_at,
               conversation.updated_at,
               conversation.last_message_at,
               latest.text_content,
               latest.status
        FROM chat_conversations conversation
        LEFT JOIN LATERAL (
          SELECT text_content, status
          FROM chat_messages
          WHERE conversation_id = conversation.conversation_id
          ORDER BY sequence DESC
          LIMIT 1
        ) latest ON TRUE
        """;
}
