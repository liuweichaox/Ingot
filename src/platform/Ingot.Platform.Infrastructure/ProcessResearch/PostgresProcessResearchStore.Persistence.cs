// 提供 PostgreSQL 存储实现共享的查询、事务和序列化操作。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    private async Task<ResearchPage<T>> ListPageAsync<T>(
        string sql,
        Guid projectId,
        string? cursor,
        int limit,
        Func<T, DateTimeOffset> timestamp,
        Func<T, Guid> id,
        CancellationToken ct)
    {
        DateTimeOffset? beforeTime = null;
        Guid? beforeId = null;
        if (cursor is not null)
        {
            if (!ResearchPageCursor.TryDecode(cursor, out var decodedTime, out var decodedId))
                throw new ProcessResearchRuleException("分页游标无效或已经损坏。");
            beforeTime = decodedTime;
            beforeId = decodedId;
        }
        limit = Math.Clamp(limit, 1, 200);
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)beforeTime ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = (object?)beforeId ?? DBNull.Value
        });
        command.Parameters.AddWithValue(limit + 1);
        var values = new List<T>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        var hasMore = values.Count > limit;
        if (hasMore) values.RemoveAt(values.Count - 1);
        var last = hasMore ? values[^1] : default;
        return new ResearchPage<T>
        {
            Items = values,
            NextCursor = last is null ? null : ResearchPageCursor.Encode(timestamp(last), id(last))
        };
    }

    private async Task<T?> GetOneAsync<T>(
        string sql,
        object parameter,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameter);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? default
            : Deserialize<T>((string)payload);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        object? parameter,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        if (parameter is not null)
            command.Parameters.AddWithValue(parameter);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private static async Task SaveChildAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid id,
        Guid projectId,
        string status,
        T value,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(status);
        AddJson(command, value);
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(updatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task SyncEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string resourceType,
        string resourceId,
        IReadOnlyList<EvidenceReference> evidence,
        CancellationToken ct)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText =
            "DELETE FROM research_evidence WHERE resource_type = $1 AND resource_id = $2";
        delete.Parameters.AddWithValue(resourceType);
        delete.Parameters.AddWithValue(resourceId);
        await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var item in evidence
                     .GroupBy(static value => (value.Kind, value.ReferenceId))
                     .Select(static group => group.First()))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO research_evidence
                  (evidence_id, project_id, resource_type, resource_id, kind,
                   reference_id, content_hash, payload, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """;
            insert.Parameters.AddWithValue(item.EvidenceId);
            insert.Parameters.AddWithValue(item.ProjectId);
            insert.Parameters.AddWithValue(resourceType);
            insert.Parameters.AddWithValue(resourceId);
            insert.Parameters.AddWithValue(item.Kind);
            insert.Parameters.AddWithValue(item.ReferenceId);
            insert.Parameters.AddWithValue(item.ContentHash);
            AddJson(insert, item);
            insert.Parameters.AddWithValue(item.CreatedAt);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResearchAuditEntry audit,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO process_research_audit
              (entry_id, project_id, resource_type, resource_id, action, payload, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """;
        command.Parameters.AddWithValue(audit.EntryId);
        command.Parameters.AddWithValue(audit.ProjectId);
        command.Parameters.AddWithValue(audit.ResourceType);
        command.Parameters.AddWithValue(audit.ResourceId);
        command.Parameters.AddWithValue(audit.Action);
        AddJson(command, audit);
        command.Parameters.AddWithValue(audit.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(value, JsonOptions));

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法解析 {typeof(T).Name}。");

    private static JsonSerializerOptions CreateJsonOptions()
        => new(JsonSerializerDefaults.Web);

}
