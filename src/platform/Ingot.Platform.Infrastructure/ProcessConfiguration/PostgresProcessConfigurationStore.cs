using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessConfiguration;

public sealed class PostgresProcessConfigurationStore : IProcessConfigurationStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresProcessConfigurationStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
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
            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS process_data_models (
                  model_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (model_id, version),
                  CHECK (version > 0)
                );

                CREATE TABLE IF NOT EXISTS recipe_versions (
                  recipe_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  data_model_id TEXT NOT NULL,
                  data_model_version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (recipe_id, version),
                  CHECK (version > 0),
                  CHECK (data_model_version > 0)
                );
                CREATE INDEX IF NOT EXISTS idx_recipe_versions_model
                  ON recipe_versions(data_model_id, data_model_version);

                CREATE TABLE IF NOT EXISTS process_analysis_plans (
                  plan_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  data_model_id TEXT NOT NULL,
                  data_model_version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (plan_id, version),
                  CHECK (version > 0),
                  CHECK (data_model_version > 0)
                );
                CREATE INDEX IF NOT EXISTS idx_process_analysis_plans_model
                  ON process_analysis_plans(data_model_id, data_model_version);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
        => UpsertAsync(
            "process_data_models", "model_id", value.ModelId, value.Version, value.Status,
            null, null, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
        => ListAsync<ProcessDataModel>("process_data_models", "ORDER BY model_id, version DESC", ct);

    public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
        => GetAsync<ProcessDataModel>("process_data_models", "model_id", modelId, version, ct);

    public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default)
        => DeleteAsync("process_data_models", "model_id", modelId, version, ct);

    public Task<RecipeVersion> UpsertRecipeVersionAsync(RecipeVersion value, CancellationToken ct = default)
        => UpsertAsync(
            "recipe_versions", "recipe_id", value.RecipeId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<RecipeVersion>> ListRecipeVersionsAsync(CancellationToken ct = default)
        => ListAsync<RecipeVersion>("recipe_versions", "ORDER BY recipe_id, version DESC", ct);

    public Task<RecipeVersion?> GetRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default)
        => GetAsync<RecipeVersion>("recipe_versions", "recipe_id", recipeId, version, ct);

    public Task<bool> DeleteRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default)
        => DeleteAsync("recipe_versions", "recipe_id", recipeId, version, ct);

    public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
        => UpsertAsync(
            "process_analysis_plans", "plan_id", value.PlanId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
        => ListAsync<ProcessAnalysisPlan>("process_analysis_plans", "ORDER BY plan_id, version DESC", ct);

    public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
        => GetAsync<ProcessAnalysisPlan>("process_analysis_plans", "plan_id", planId, version, ct);

    public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
        => DeleteAsync("process_analysis_plans", "plan_id", planId, version, ct);

    private async Task<T> UpsertAsync<T>(
        string table,
        string keyColumn,
        string key,
        int version,
        string status,
        string? modelId,
        int? modelVersion,
        T payload,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var hasModel = modelId is not null && modelVersion.HasValue;
        var columns = hasModel
            ? $"{keyColumn}, version, data_model_id, data_model_version, status, payload, updated_at"
            : $"{keyColumn}, version, status, payload, updated_at";
        var values = hasModel
            ? "@key, @version, @model_id, @model_version, @status, @payload, @updated_at"
            : "@key, @version, @status, @payload, @updated_at";
        var updates = hasModel
            ? "data_model_id = EXCLUDED.data_model_id, data_model_version = EXCLUDED.data_model_version, status = EXCLUDED.status, payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at"
            : "status = EXCLUDED.status, payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at";
        await using var command = _dataSource.CreateCommand(
            $"INSERT INTO {table}({columns}) VALUES ({values}) ON CONFLICT ({keyColumn}, version) DO UPDATE SET {updates};");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("status", status);
        if (hasModel)
        {
            command.Parameters.AddWithValue("model_id", modelId!);
            command.Parameters.AddWithValue("model_version", modelVersion!.Value);
        }
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return payload;
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string table, string orderBy, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"SELECT payload::text FROM {table} {orderBy};");
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    private async Task<T?> GetAsync<T>(string table, string keyColumn, string key, int version, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"SELECT payload::text FROM {table} WHERE {keyColumn} = @key AND version = @version;");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", version);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull ? default : JsonSerializer.Deserialize<T>((string)payload, JsonOptions);
    }

    private async Task<bool> DeleteAsync(string table, string keyColumn, string key, int version, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE {keyColumn} = @key AND version = @version;");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
