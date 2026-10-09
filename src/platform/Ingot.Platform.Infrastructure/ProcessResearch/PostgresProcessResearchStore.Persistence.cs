// 提供 PostgreSQL 存储实现共享的查询、分页和序列化操作。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    /// <summary>
    /// 键集分页：SQL 中 @before_at、@before_id 为上一页最后一行，@take 为本页条数加一。
    /// </summary>
    private async Task<ResearchPage<T>> ListPageAsync<T>(
        string sql,
        Action<NpgsqlCommand> bind,
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
        bind(command);
        AddNullable(command, "before_at", NpgsqlDbType.TimestampTz, beforeTime);
        AddNullable(command, "before_id", NpgsqlDbType.Uuid, beforeId);
        command.Parameters.AddWithValue("take", limit + 1);
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
        Action<NpgsqlCommand> bind,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        bind(command);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? default
            : Deserialize<T>((string)payload);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        Action<NpgsqlCommand> bind,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        bind(command);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private static void AddJson<T>(NpgsqlCommand command, string name, T value)
        => command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(value, JsonOptions));

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法解析 {typeof(T).Name}。");
}
