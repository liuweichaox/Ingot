using System.Text.Json;
using Ingot.Contracts.Inspections;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class PostgresInspectionMasterDataStore : IInspectionMasterDataStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresInspectionMasterDataStore(IConfiguration configuration)
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
                CREATE TABLE IF NOT EXISTS inspection_definitions (
                  code TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (code, version),
                  CHECK (version > 0)
                );

                CREATE TABLE IF NOT EXISTS phase_definitions (
                  code TEXT PRIMARY KEY,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS phase_mappings (
                  mapping_id TEXT PRIMARY KEY,
                  recipe_id TEXT NOT NULL,
                  recipe_version TEXT,
                  recipe_template TEXT,
                  recipe_step TEXT NOT NULL,
                  phase_code TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_phase_mappings_lookup
                  ON phase_mappings(recipe_id, recipe_version, recipe_template, recipe_step);

                CREATE TABLE IF NOT EXISTS feature_definitions (
                  code TEXT PRIMARY KEY,
                  phase_code TEXT NOT NULL,
                  signal TEXT NOT NULL,
                  aggregation TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_feature_definitions_phase
                  ON feature_definitions(phase_code);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
            await SeedDefaultOpticalMoldingAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

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
              mapping_id, recipe_id, recipe_version, recipe_template, recipe_step, phase_code, payload, updated_at)
            VALUES (
              @mapping_id, @recipe_id, @recipe_version, @recipe_template, @recipe_step, @phase_code, @payload, @updated_at)
            ON CONFLICT (mapping_id) DO UPDATE SET
              recipe_id = EXCLUDED.recipe_id,
              recipe_version = EXCLUDED.recipe_version,
              recipe_template = EXCLUDED.recipe_template,
              recipe_step = EXCLUDED.recipe_step,
              phase_code = EXCLUDED.phase_code,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("mapping_id", mapping.MappingId);
        command.Parameters.AddWithValue("recipe_id", mapping.RecipeId);
        command.Parameters.AddWithValue("recipe_version", (object?)mapping.RecipeVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("recipe_template", (object?)mapping.RecipeTemplate ?? DBNull.Value);
        command.Parameters.AddWithValue("recipe_step", mapping.RecipeStep);
        command.Parameters.AddWithValue("phase_code", mapping.PhaseCode);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(mapping, JsonOptions));
        command.Parameters.AddWithValue("updated_at", mapping.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return mapping;
    }

    public Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default)
        => ListAsync<PhaseMapping>("phase_mappings", "ORDER BY recipe_id, recipe_version, recipe_step", ct);

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
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task SeedDefaultOpticalMoldingAsync(CancellationToken ct)
    {
        await using var countCommand = _dataSource.CreateCommand(
            "SELECT count(*) FROM inspection_definitions;");
        var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
        if (count > 0)
            return;

        var phases = new[]
        {
            new PhaseDefinition { Code = "preheat", Name = "预热", SortOrder = 10, Required = true, UpdatedAt = DateTimeOffset.UtcNow },
            new PhaseDefinition { Code = "soak", Name = "均热", SortOrder = 20, Required = true, UpdatedAt = DateTimeOffset.UtcNow },
            new PhaseDefinition { Code = "press", Name = "压制", SortOrder = 30, Required = true, UpdatedAt = DateTimeOffset.UtcNow },
            new PhaseDefinition { Code = "anneal", Name = "退火", SortOrder = 40, Required = true, UpdatedAt = DateTimeOffset.UtcNow },
            new PhaseDefinition { Code = "cool", Name = "冷却", SortOrder = 50, Required = false, UpdatedAt = DateTimeOffset.UtcNow }
        };
        foreach (var phase in phases)
            await UpsertPhaseDefinitionAsync(phase, ct).ConfigureAwait(false);

        var inspection = new InspectionDefinition
        {
            Code = "optical.surface",
            Version = 1,
            Name = "光学镜片模压检测",
            Description = "首站默认检测定义，限值需由工艺工程师核定。",
            UpdatedAt = DateTimeOffset.UtcNow,
            Characteristics =
            [
                new InspectionCharacteristicDefinition { Code = "surface.pv_um", Name = "面形 PV", InputType = "numeric", Unit = "um", Required = true },
                new InspectionCharacteristicDefinition { Code = "surface.rms_um", Name = "面形 RMS", InputType = "numeric", Unit = "um", Required = false },
                new InspectionCharacteristicDefinition { Code = "center_thickness_mm", Name = "中心厚度", InputType = "numeric", Unit = "mm", Required = false },
                new InspectionCharacteristicDefinition { Code = "decentration_um", Name = "偏心", InputType = "numeric", Unit = "um", Required = false },
                new InspectionCharacteristicDefinition { Code = "roughness_nm", Name = "表面粗糙度", InputType = "numeric", Unit = "nm", Required = false },
                new InspectionCharacteristicDefinition { Code = "defect_count", Name = "条纹/气泡计数", InputType = "numeric", Unit = "1", LowerLimit = 0, Required = false }
            ]
        };
        if (InspectionMasterDataValidator.TryValidate(inspection, out var normalizedInspection, out _))
            await UpsertInspectionDefinitionAsync(normalizedInspection!, ct).ConfigureAwait(false);

        var features = new[]
        {
            new FeatureDefinition { Code = "mold.temp.peak_c", Name = "峰值模温", PhaseCode = "press", Signal = "mold.temperature_c", Aggregation = "max", Unit = "Cel" },
            new FeatureDefinition { Code = "mold.temp.uniformity_c", Name = "上下模温差", PhaseCode = "press", Signal = "mold.temperature_c", Aggregation = "range_across", Unit = "Cel" },
            new FeatureDefinition { Code = "soak.dwell_above_tg_ms", Name = "均热驻留", PhaseCode = "soak", Signal = "mold.temperature_c", Aggregation = "dwell", Unit = "ms" },
            new FeatureDefinition { Code = "press.force_peak_n", Name = "压制力峰值", PhaseCode = "press", Signal = "press.force_n", Aggregation = "max", Unit = "N" },
            new FeatureDefinition { Code = "press.force_impulse", Name = "保压冲量", PhaseCode = "press", Signal = "press.force_n", Aggregation = "integral" },
            new FeatureDefinition { Code = "press.rate_mm_per_s", Name = "压制速度", PhaseCode = "press", Signal = "press.position_mm", Aggregation = "slope", Unit = "mm/s" },
            new FeatureDefinition { Code = "anneal.rate_c_per_min", Name = "退火速率", PhaseCode = "anneal", Signal = "mold.temperature_c", Aggregation = "slope", Unit = "Cel/min" },
            new FeatureDefinition { Code = "anneal.rate_deviation", Name = "退火速率偏差", PhaseCode = "anneal", Signal = "mold.temperature_c", Aggregation = "slope_deviation", Unit = "Cel/min" },
            new FeatureDefinition { Code = "cool.rate_c_per_min", Name = "冷却速率", PhaseCode = "cool", Signal = "mold.temperature_c", Aggregation = "slope", Unit = "Cel/min" },
            new FeatureDefinition { Code = "atmosphere.o2_ppm_max", Name = "氧含量最大值", PhaseCode = "cycle", Signal = "atmosphere.o2_ppm", Aggregation = "max", Unit = "ppm" },
            new FeatureDefinition { Code = "thermal.budget", Name = "等效热负荷", PhaseCode = "cycle", Signal = "mold.temperature_c", Aggregation = "integral" }
        };
        foreach (var feature in features)
        {
            if (InspectionMasterDataValidator.TryValidate(feature, out var normalizedFeature, out _))
                await UpsertFeatureDefinitionAsync(normalizedFeature!, ct).ConfigureAwait(false);
        }
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
