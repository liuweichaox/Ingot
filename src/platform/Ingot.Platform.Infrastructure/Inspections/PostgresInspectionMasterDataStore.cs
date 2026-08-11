using System.Text.Json;
using Ingot.Contracts.Inspections;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class PostgresInspectionMasterDataStore : IInspectionMasterDataStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresInspectionMasterDataStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<InspectionDefinition> UpsertInspectionDefinitionAsync(InspectionDefinition definition, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await UpsertAsync(
            "inspection_definitions",
            "code, version, payload, updated_at",
            "code = @code, version = @version",
            "code, version",
            definition.Code,
            definition.Version,
            definition,
            definition.UpdatedAt,
            ct).ConfigureAwait(false);
        return definition;
    }

    public Task<IReadOnlyList<InspectionDefinition>> ListInspectionDefinitionsAsync(CancellationToken ct = default)
        => ListAsync<InspectionDefinition>("inspection_definitions", "ORDER BY code, version DESC", ct);

    public Task<InspectionDefinition?> GetInspectionDefinitionAsync(string code, int version, CancellationToken ct = default)
        => GetAsync<InspectionDefinition>(
            "inspection_definitions",
            "code = @code AND version = @version",
            command =>
            {
                command.Parameters.AddWithValue("code", code);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<bool> DeleteInspectionDefinitionAsync(string code, int version, CancellationToken ct = default)
        => DeleteAsync(
            "inspection_definitions",
            "code = @code AND version = @version",
            command =>
            {
                command.Parameters.AddWithValue("code", code);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<InspectionPlan> UpsertInspectionPlanAsync(InspectionPlan plan, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await UpsertAsync(
            "inspection_plans",
            "plan_id, version, payload, updated_at",
            "plan_id = @code, version = @version",
            "plan_id, version",
            plan.PlanId,
            plan.Version,
            plan,
            plan.UpdatedAt,
            ct).ConfigureAwait(false);
        return plan;
    }

    public Task<IReadOnlyList<InspectionPlan>> ListInspectionPlansAsync(CancellationToken ct = default)
        => ListAsync<InspectionPlan>("inspection_plans", "ORDER BY plan_id, version DESC", ct);

    public Task<InspectionPlan?> GetInspectionPlanAsync(string planId, int version, CancellationToken ct = default)
        => GetAsync<InspectionPlan>(
            "inspection_plans",
            "plan_id = @plan_id AND version = @version",
            command =>
            {
                command.Parameters.AddWithValue("plan_id", planId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<bool> DeleteInspectionPlanAsync(string planId, int version, CancellationToken ct = default)
        => DeleteAsync(
            "inspection_plans",
            "plan_id = @plan_id AND version = @version",
            command =>
            {
                command.Parameters.AddWithValue("plan_id", planId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<PhaseDefinition> UpsertPhaseDefinitionAsync(PhaseDefinition definition, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await UpsertSingleAsync("phase_definitions", "code", definition.Code, definition, definition.UpdatedAt, ct)
            .ConfigureAwait(false);
        return definition;
    }

    public Task<IReadOnlyList<PhaseDefinition>> ListPhaseDefinitionsAsync(CancellationToken ct = default)
        => ListAsync<PhaseDefinition>("phase_definitions", "ORDER BY (payload->>'sortOrder')::int, code", ct);

    public Task<PhaseDefinition?> GetPhaseDefinitionAsync(string code, CancellationToken ct = default)
        => GetSingleAsync<PhaseDefinition>("phase_definitions", "code", code, ct);

    public Task<bool> DeletePhaseDefinitionAsync(string code, CancellationToken ct = default)
        => DeleteSingleAsync("phase_definitions", "code", code, ct);

    public async Task<PhaseMapping> UpsertPhaseMappingAsync(PhaseMapping mapping, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO phase_mappings(
              mapping_id, process_specification_id, process_specification_version, process_template, process_step, phase_code, payload, updated_at)
            VALUES (
              @mapping_id, @process_specification_id, @process_specification_version, @process_template, @process_step, @phase_code, @payload, @updated_at)
            ON CONFLICT (mapping_id) DO UPDATE SET
              process_specification_id = EXCLUDED.process_specification_id,
              process_specification_version = EXCLUDED.process_specification_version,
              process_template = EXCLUDED.process_template,
              process_step = EXCLUDED.process_step,
              phase_code = EXCLUDED.phase_code,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("mapping_id", mapping.MappingId);
        command.Parameters.AddWithValue("process_specification_id", mapping.ProcessSpecificationId);
        command.Parameters.AddWithValue("process_specification_version", (object?)mapping.ProcessSpecification ?? DBNull.Value);
        command.Parameters.AddWithValue("process_template", (object?)mapping.ProcessTemplate ?? DBNull.Value);
        command.Parameters.AddWithValue("process_step", mapping.ProcessStep);
        command.Parameters.AddWithValue("phase_code", mapping.PhaseCode);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(mapping, JsonOptions));
        command.Parameters.AddWithValue("updated_at", mapping.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return mapping;
    }

    public Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default)
        => ListAsync<PhaseMapping>("phase_mappings", "ORDER BY process_specification_id, process_specification_version, process_step", ct);

    public Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default)
        => GetSingleAsync<PhaseMapping>("phase_mappings", "mapping_id", mappingId, ct);

    public Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default)
        => DeleteSingleAsync("phase_mappings", "mapping_id", mappingId, ct);

    public async Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition definition, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO feature_definitions(code, phase_code, signal, aggregation, payload, updated_at)
            VALUES (@code, @phase_code, @signal, @aggregation, @payload, @updated_at)
            ON CONFLICT (code) DO UPDATE SET
              phase_code = EXCLUDED.phase_code,
              signal = EXCLUDED.signal,
              aggregation = EXCLUDED.aggregation,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("code", definition.Code);
        command.Parameters.AddWithValue("phase_code", definition.PhaseCode);
        command.Parameters.AddWithValue("signal", definition.Signal);
        command.Parameters.AddWithValue("aggregation", definition.Aggregation);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(definition, JsonOptions));
        command.Parameters.AddWithValue("updated_at", definition.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return definition;
    }

    public Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default)
        => ListAsync<FeatureDefinition>("feature_definitions", "ORDER BY phase_code, code", ct);

    public Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default)
        => GetSingleAsync<FeatureDefinition>("feature_definitions", "code", code, ct);

    public Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default)
        => DeleteSingleAsync("feature_definitions", "code", code, ct);

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task UpsertSingleAsync<T>(
        string table,
        string keyColumn,
        string key,
        T payload,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            $"""
             INSERT INTO {table}({keyColumn}, payload, updated_at)
             VALUES (@key, @payload, @updated_at)
             ON CONFLICT ({keyColumn}) DO UPDATE SET
               payload = EXCLUDED.payload,
               updated_at = EXCLUDED.updated_at;
             """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertAsync<T>(
        string table,
        string columns,
        string conflictTarget,
        string conflictColumns,
        string code,
        int version,
        T payload,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            $"""
             INSERT INTO {table}({columns})
             VALUES (@code, @version, @payload, @updated_at)
             ON CONFLICT ({conflictColumns}) DO UPDATE SET
               payload = EXCLUDED.payload,
               updated_at = EXCLUDED.updated_at;
             """);
        _ = conflictTarget;
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string table, string orderBy, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"SELECT payload::text FROM {table} {orderBy};");
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    private async Task<T?> GetSingleAsync<T>(string table, string keyColumn, string key, CancellationToken ct)
        => await GetAsync<T>(
            table,
            $"{keyColumn} = @key",
            command => command.Parameters.AddWithValue("key", key),
            ct).ConfigureAwait(false);

    private async Task<T?> GetAsync<T>(
        string table,
        string predicate,
        Action<NpgsqlCommand> bind,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"SELECT payload::text FROM {table} WHERE {predicate};");
        bind(command);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? default : JsonSerializer.Deserialize<T>((string)value, JsonOptions);
    }

    private Task<bool> DeleteSingleAsync(string table, string keyColumn, string key, CancellationToken ct)
        => DeleteAsync(
            table,
            $"{keyColumn} = @key",
            command => command.Parameters.AddWithValue("key", key),
            ct);

    private async Task<bool> DeleteAsync(
        string table,
        string predicate,
        Action<NpgsqlCommand> bind,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE {predicate};");
        bind(command);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }
}
