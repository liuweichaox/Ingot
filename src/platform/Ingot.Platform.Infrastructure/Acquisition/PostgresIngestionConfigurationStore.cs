// 实现 PostgresIngestionConfigurationStore 的 PostgreSQL 持久化适配，避免数据库细节泄漏到应用层。

using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Platform.Application.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class PostgresIngestionConfigurationStore : IIngestionConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIngestionConfigurationStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task<IReadOnlyList<IngestionTaskTemplate>> ListTemplatesAsync(CancellationToken ct = default)
        => ListAsync<IngestionTaskTemplate>(
            "SELECT payload::text FROM ingestion_task_templates ORDER BY template_id, version DESC;", ct);

    public Task<IngestionTaskTemplate?> GetTemplateAsync(
        string templateId,
        int version,
        CancellationToken ct = default)
        => GetAsync<IngestionTaskTemplate>(
            "SELECT payload::text FROM ingestion_task_templates WHERE template_id = @id AND version = @version;",
            templateId,
            version,
            ct);

    public Task<IngestionTaskTemplate> UpsertTemplateAsync(
        IngestionTaskTemplate value,
        CancellationToken ct = default)
        => UpsertTemplateCoreAsync(value, publish: false, ct);

    public Task<IngestionTaskTemplate> PublishTemplateExclusiveAsync(
        IngestionTaskTemplate value,
        CancellationToken ct = default)
        => UpsertTemplateCoreAsync(value, publish: true, ct);

    public Task<bool> DeleteTemplateAsync(string templateId, int version, CancellationToken ct = default)
        => DeleteAsync(
            "DELETE FROM ingestion_task_templates WHERE template_id = @id AND version = @version AND status = 'draft';",
            templateId,
            version,
            ct);

    public Task<IReadOnlyList<DataSourceInstance>> ListDataSourcesAsync(CancellationToken ct = default)
        => ListAsync<DataSourceInstance>(
            "SELECT payload::text FROM data_source_instances ORDER BY data_source_id, version DESC;", ct);

    public Task<DataSourceInstance?> GetDataSourceAsync(
        string dataSourceId,
        int version,
        CancellationToken ct = default)
        => GetAsync<DataSourceInstance>(
            "SELECT payload::text FROM data_source_instances WHERE data_source_id = @id AND version = @version;",
            dataSourceId,
            version,
            ct);

    public Task<DataSourceInstance> UpsertDataSourceAsync(
        DataSourceInstance value,
        CancellationToken ct = default)
        => UpsertDataSourceCoreAsync(value, publish: false, ct);

    public Task<DataSourceInstance> PublishDataSourceExclusiveAsync(
        DataSourceInstance value,
        CancellationToken ct = default)
        => UpsertDataSourceCoreAsync(value, publish: true, ct);

    public async Task<IReadOnlyList<DataSourceInstance>> SaveDataSourcesAsync(
        IReadOnlyList<DataSourceInstance> values,
        CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var value in values.OrderBy(static item => item.DataSourceId, StringComparer.Ordinal))
        {
            await AcquireLockAsync(connection, transaction, "data-source", value.DataSourceId, ct).ConfigureAwait(false);
            await EnsureMutableVersionAsync(
                connection, transaction, "data_source_instances", "data_source_id",
                value.DataSourceId, value.Version, value.Status, ct).ConfigureAwait(false);
            if (value.Status == ConfigurationStatuses.Published)
            {
                var prior = await ReadPublishedAsync<DataSourceInstance>(
                    connection, transaction, "data_source_instances", "data_source_id",
                    value.DataSourceId, value.Version, ct).ConfigureAwait(false);
                foreach (var old in prior)
                    await WriteDataSourceAsync(
                        connection,
                        transaction,
                        old with { Status = ConfigurationStatuses.Retired, UpdatedAt = value.UpdatedAt },
                        ct).ConfigureAwait(false);
            }
            await WriteDataSourceAsync(connection, transaction, value, ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return values;
    }

    public Task<bool> DeleteDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default)
        => DeleteAsync(
            "DELETE FROM data_source_instances WHERE data_source_id = @id AND version = @version AND status = 'draft';",
            dataSourceId,
            version,
            ct);

    public Task<IReadOnlyList<IngestionTaskBinding>> ListBindingsAsync(CancellationToken ct = default)
        => ListAsync<IngestionTaskBinding>(
            "SELECT payload::text FROM ingestion_task_bindings ORDER BY task_id, version DESC;", ct);

    public Task<IngestionTaskBinding?> GetBindingAsync(
        string taskId,
        int version,
        CancellationToken ct = default)
        => GetAsync<IngestionTaskBinding>(
            "SELECT payload::text FROM ingestion_task_bindings WHERE task_id = @id AND version = @version;",
            taskId,
            version,
            ct);

    public async Task<IReadOnlyList<IngestionTask>> SaveMaterializedTasksAsync(
        IReadOnlyList<(IngestionTaskBinding Binding, IngestionTask Task)> values,
        CancellationToken ct = default)
    {
        if (values.Count == 0) return [];
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var (binding, task) in values
                     .OrderBy(static item => item.Task.TaskId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Task.Version))
        {
            await AcquireLockAsync(connection, transaction, "ingestion-task", task.TaskId, ct).ConfigureAwait(false);
            await EnsureMutableVersionAsync(
                connection, transaction, "ingestion_task_bindings", "task_id",
                binding.TaskId, binding.Version, binding.Status, ct).ConfigureAwait(false);
            await EnsureMutableVersionAsync(
                connection, transaction, "ingestion_tasks", "task_id",
                task.TaskId, task.Version, task.Status, ct).ConfigureAwait(false);
            if (task.Status == ConfigurationStatuses.Published)
            {
                await RetireTaskVersionsAsync(connection, transaction, task, ct).ConfigureAwait(false);
                await RetireBindingVersionsAsync(connection, transaction, binding, ct).ConfigureAwait(false);
            }
            await UpsertBindingAsync(connection, transaction, binding, ct).ConfigureAwait(false);
            await UpsertTaskAsync(connection, transaction, task, ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return values.Select(static item => item.Task).ToArray();
    }

    public async Task<ReusableIngestionConfiguration> SaveExtractedAsync(
        ReusableIngestionConfiguration value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireLockAsync(connection, transaction, "template", value.Template.TemplateId, ct).ConfigureAwait(false);
        await AcquireLockAsync(connection, transaction, "data-source", value.DataSource.DataSourceId, ct).ConfigureAwait(false);
        await AcquireLockAsync(connection, transaction, "ingestion-task", value.Task.TaskId, ct).ConfigureAwait(false);
        await EnsureVersionAbsentAsync(connection, transaction, "ingestion_task_templates", "template_id", value.Template.TemplateId, value.Template.Version, ct)
            .ConfigureAwait(false);
        await EnsureVersionAbsentAsync(connection, transaction, "data_source_instances", "data_source_id", value.DataSource.DataSourceId, value.DataSource.Version, ct)
            .ConfigureAwait(false);
        await EnsureVersionAbsentAsync(connection, transaction, "ingestion_task_bindings", "task_id", value.Binding.TaskId, value.Binding.Version, ct)
            .ConfigureAwait(false);
        await EnsureVersionAbsentAsync(connection, transaction, "ingestion_tasks", "task_id", value.Task.TaskId, value.Task.Version, ct)
            .ConfigureAwait(false);
        var previousTemplates = await ReadPublishedAsync<IngestionTaskTemplate>(
            connection, transaction, "ingestion_task_templates", "template_id",
            value.Template.TemplateId, value.Template.Version, ct).ConfigureAwait(false);
        foreach (var previous in previousTemplates)
            await WriteTemplateAsync(connection, transaction,
                previous with { Status = ConfigurationStatuses.Retired, UpdatedAt = value.Template.UpdatedAt }, ct)
                .ConfigureAwait(false);
        var previousSources = await ReadPublishedAsync<DataSourceInstance>(
            connection, transaction, "data_source_instances", "data_source_id",
            value.DataSource.DataSourceId, value.DataSource.Version, ct).ConfigureAwait(false);
        foreach (var previous in previousSources)
            await WriteDataSourceAsync(connection, transaction,
                previous with { Status = ConfigurationStatuses.Retired, UpdatedAt = value.DataSource.UpdatedAt }, ct)
                .ConfigureAwait(false);
        await WriteTemplateAsync(connection, transaction, value.Template, ct).ConfigureAwait(false);
        await WriteDataSourceAsync(connection, transaction, value.DataSource, ct).ConfigureAwait(false);
        await RetireTaskVersionsAsync(connection, transaction, value.Task, ct).ConfigureAwait(false);
        await RetireBindingVersionsAsync(connection, transaction, value.Binding, ct).ConfigureAwait(false);
        await UpsertBindingAsync(connection, transaction, value.Binding, ct).ConfigureAwait(false);
        await UpsertTaskAsync(connection, transaction, value.Task, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    private async Task<IngestionTaskTemplate> UpsertTemplateCoreAsync(
        IngestionTaskTemplate value,
        bool publish,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (value.Status != (publish ? ConfigurationStatuses.Published : ConfigurationStatuses.Draft))
            throw new InvalidOperationException("任务模板只能保存草稿或通过发布操作进入已发布状态。");
        await AcquireLockAsync(connection, transaction, "template", value.TemplateId, ct).ConfigureAwait(false);
        await EnsureMutableVersionAsync(
            connection, transaction, "ingestion_task_templates", "template_id",
            value.TemplateId, value.Version, value.Status, ct).ConfigureAwait(false);
        if (publish)
        {
            var prior = await ReadPublishedAsync<IngestionTaskTemplate>(
                connection,
                transaction,
                "ingestion_task_templates",
                "template_id",
                value.TemplateId,
                value.Version,
                ct).ConfigureAwait(false);
            foreach (var old in prior)
                await WriteTemplateAsync(
                    connection,
                    transaction,
                    old with { Status = ConfigurationStatuses.Retired, UpdatedAt = value.UpdatedAt },
                    ct).ConfigureAwait(false);
        }
        await WriteTemplateAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    private async Task<DataSourceInstance> UpsertDataSourceCoreAsync(
        DataSourceInstance value,
        bool publish,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (value.Status != (publish ? ConfigurationStatuses.Published : ConfigurationStatuses.Draft))
            throw new InvalidOperationException("数据源只能保存草稿或通过发布操作进入已发布状态。");
        await AcquireLockAsync(connection, transaction, "data-source", value.DataSourceId, ct).ConfigureAwait(false);
        await EnsureMutableVersionAsync(
            connection, transaction, "data_source_instances", "data_source_id",
            value.DataSourceId, value.Version, value.Status, ct).ConfigureAwait(false);
        if (publish)
        {
            var prior = await ReadPublishedAsync<DataSourceInstance>(
                connection,
                transaction,
                "data_source_instances",
                "data_source_id",
                value.DataSourceId,
                value.Version,
                ct).ConfigureAwait(false);
            foreach (var old in prior)
                await WriteDataSourceAsync(
                    connection,
                    transaction,
                    old with { Status = ConfigurationStatuses.Retired, UpdatedAt = value.UpdatedAt },
                    ct).ConfigureAwait(false);
        }
        await WriteDataSourceAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    private static async Task WriteTemplateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IngestionTaskTemplate value,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ingestion_task_templates(template_id, version, status, protocol, payload, updated_at)
            VALUES (@id, @version, @status, @protocol, @payload, @updated_at)
            ON CONFLICT (template_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              protocol = EXCLUDED.protocol,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", value.TemplateId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("protocol", value.Protocol);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task WriteDataSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DataSourceInstance value,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO data_source_instances(data_source_id, version, edge_id, status, protocol, payload, updated_at)
            VALUES (@id, @version, @edge_id, @status, @protocol, @payload, @updated_at)
            ON CONFLICT (data_source_id, version) DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              status = EXCLUDED.status,
              protocol = EXCLUDED.protocol,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", value.DataSourceId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("edge_id", value.EdgeId);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("protocol", value.Protocol);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertBindingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IngestionTaskBinding value,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ingestion_task_bindings(
              task_id, version, template_id, template_version,
              data_source_id, data_source_version, status, payload, updated_at)
            VALUES (
              @task_id, @version, @template_id, @template_version,
              @data_source_id, @data_source_version, @status, @payload, @updated_at)
            ON CONFLICT (task_id, version) DO UPDATE SET
              template_id = EXCLUDED.template_id,
              template_version = EXCLUDED.template_version,
              data_source_id = EXCLUDED.data_source_id,
              data_source_version = EXCLUDED.data_source_version,
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("task_id", value.TaskId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("template_id", value.TemplateId);
        command.Parameters.AddWithValue("template_version", value.TemplateVersion);
        command.Parameters.AddWithValue("data_source_id", value.DataSourceId);
        command.Parameters.AddWithValue("data_source_version", value.DataSourceVersion);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IngestionTask value,
        CancellationToken ct)
    {
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
    }

    private static async Task RetireTaskVersionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IngestionTask current,
        CancellationToken ct)
    {
        var prior = await ReadPublishedAsync<IngestionTask>(
            connection,
            transaction,
            "ingestion_tasks",
            "task_id",
            current.TaskId,
            current.Version,
            ct).ConfigureAwait(false);
        foreach (var old in prior)
            await UpsertTaskAsync(
                connection,
                transaction,
                old with { Status = ConfigurationStatuses.Retired, UpdatedAt = current.UpdatedAt },
                ct).ConfigureAwait(false);
    }

    private static async Task RetireBindingVersionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IngestionTaskBinding current,
        CancellationToken ct)
    {
        var prior = await ReadPublishedAsync<IngestionTaskBinding>(
            connection,
            transaction,
            "ingestion_task_bindings",
            "task_id",
            current.TaskId,
            current.Version,
            ct).ConfigureAwait(false);
        foreach (var old in prior)
            await UpsertBindingAsync(
                connection,
                transaction,
                old with { Status = ConfigurationStatuses.Retired, UpdatedAt = current.UpdatedAt },
                ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string sql, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    private async Task<T?> GetAsync<T>(string sql, string id, int version, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? default
            : JsonSerializer.Deserialize<T>((string)payload, JsonOptions);
    }

    private async Task<bool> DeleteAsync(string sql, string id, int version, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task<IReadOnlyList<T>> ReadPublishedAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string idColumn,
        string id,
        int excludedVersion,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT payload::text FROM {table} WHERE {idColumn} = @id AND version <> @version AND status = 'published' FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", excludedVersion);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    private static async Task AcquireLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scope,
        string id,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue("lock_key", $"{scope}:{id}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureVersionAbsentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string idColumn,
        string id,
        int version,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT EXISTS (SELECT 1 FROM {table} WHERE {idColumn} = @id AND version = @version);",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false))
            throw new InvalidOperationException($"复用资产 {id} v{version} 已存在，请使用新的版本号。");
    }

    private static async Task EnsureMutableVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string idColumn,
        string id,
        int version,
        string nextStatus,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT status FROM {table} WHERE {idColumn} = @id AND version = @version FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        var current = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var currentStatus = current is null or DBNull ? null : (string)current;
        if (nextStatus is not (ConfigurationStatuses.Draft or ConfigurationStatuses.Published) ||
            currentStatus is not null and not ConfigurationStatuses.Draft)
            throw new InvalidOperationException(
                $"配置 {id} v{version} 已发布、已停用或状态转换无效，不能覆盖同一版本。");
    }

}
