using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.Acquisition;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class PostgresIngestionTaskStore : IIngestionTaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIngestionTaskStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default)
        => QueryAsync("ORDER BY task_id, version DESC", null, ct);

    public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
        => QueryAsync("WHERE edge_id = @edge_id AND status = 'published' ORDER BY task_id", edgeId, ct);

    public async Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM ingestion_tasks WHERE task_id = @task_id AND version = @version;");
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("version", version);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : JsonSerializer.Deserialize<IngestionTask>((string)payload, JsonOptions);
    }

    public async Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireLockAsync(connection, transaction, value.TaskId, ct).ConfigureAwait(false);
        var currentStatus = await ReadStatusForUpdateAsync(
            connection, transaction, value.TaskId, value.Version, ct).ConfigureAwait(false);
        var permitted = currentStatus switch
        {
            null => value.Status == ConfigurationStatuses.Draft,
            ConfigurationStatuses.Draft => value.Status == ConfigurationStatuses.Draft,
            ConfigurationStatuses.Published => value.Status == ConfigurationStatuses.Retired,
            _ => false
        };
        if (!permitted)
            throw new InvalidOperationException(
                $"任务 {value.TaskId} v{value.Version} 的状态转换无效或版本已不可修改。");

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ingestion_tasks(task_id, version, edge_id, status, payload, updated_at)
            VALUES (@task_id, @version, @edge_id, @status, @payload, @updated_at)
            ON CONFLICT (task_id, version) DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("task_id", value.TaskId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("edge_id", value.EdgeId);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "DELETE FROM ingestion_tasks WHERE task_id = @task_id AND version = @version AND status = 'draft';");
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<IngestionTask> PublishExclusiveAsync(
        IngestionTask published,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await AcquireLockAsync(connection, transaction, published.TaskId, ct).ConfigureAwait(false);
        var currentStatus = await ReadStatusForUpdateAsync(
            connection, transaction, published.TaskId, published.Version, ct).ConfigureAwait(false);
        if (currentStatus is not null and not ConfigurationStatuses.Draft)
            throw new InvalidOperationException(
                $"任务 {published.TaskId} v{published.Version} 已发布或停用，不能覆盖同一版本。");

        var retire = new List<(int Version, IngestionTask Task)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT version, payload::text FROM ingestion_tasks
            WHERE task_id = @task_id AND version <> @version AND status = 'published'
            FOR UPDATE;
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("task_id", published.TaskId);
            select.Parameters.AddWithValue("version", published.Version);
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var task = JsonSerializer.Deserialize<IngestionTask>(reader.GetString(1), JsonOptions)!;
                retire.Add((reader.GetInt32(0), task));
            }
        }

        foreach (var (version, task) in retire)
        {
            var retired = task with { Status = ConfigurationStatuses.Retired, UpdatedAt = published.UpdatedAt };
            await using var update = new NpgsqlCommand(
                """
                UPDATE ingestion_tasks
                SET status = 'retired', payload = @payload, updated_at = @updated_at
                WHERE task_id = @task_id AND version = @version;
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue("task_id", published.TaskId);
            update.Parameters.AddWithValue("version", version);
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(retired, JsonOptions));
            update.Parameters.AddWithValue("updated_at", published.UpdatedAt.UtcDateTime);
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO ingestion_tasks(task_id, version, edge_id, status, payload, updated_at)
            VALUES (@task_id, @version, @edge_id, @status, @payload, @updated_at)
            ON CONFLICT (task_id, version) DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue("task_id", published.TaskId);
            upsert.Parameters.AddWithValue("version", published.Version);
            upsert.Parameters.AddWithValue("edge_id", published.EdgeId);
            upsert.Parameters.AddWithValue("status", published.Status);
            upsert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(published, JsonOptions));
            upsert.Parameters.AddWithValue("updated_at", published.UpdatedAt.UtcDateTime);
            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return published;
    }

    private static async Task AcquireLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string taskId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@task_id, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadStatusForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string taskId,
        int version,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT status FROM ingestion_tasks WHERE task_id = @task_id AND version = @version FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("version", version);
        var status = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return status is null or DBNull ? null : (string)status;
    }

    private async Task<IReadOnlyList<IngestionTask>> QueryAsync(
        string clause,
        string? edgeId,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"SELECT payload::text FROM ingestion_tasks {clause};");
        if (edgeId is not null) command.Parameters.AddWithValue("edge_id", edgeId);
        var values = new List<IngestionTask>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<IngestionTask>(reader.GetString(0), JsonOptions)!);
        return values;
    }

}
