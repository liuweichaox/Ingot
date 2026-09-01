
using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessConfiguration;

public sealed class PostgresProcessConfigurationStore : IProcessConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessConfigurationStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

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

    public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
        => UpsertAsync(
            "process_specification_versions", "process_specification_id", value.ProcessSpecificationId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
        => ListAsync<ProcessSpecification>("process_specification_versions", "ORDER BY process_specification_id, version DESC", ct);

    public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
        => GetAsync<ProcessSpecification>("process_specification_versions", "process_specification_id", processSpecificationId, version, ct);

    public async Task<ProcessSpecificationDraftCreationResult> CreateNextProcessSpecificationDraftAsync(
        string processSpecificationId,
        int baseVersion,
        CreateProcessSpecificationDraftRequest request,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var normalizedId = NormalizeIdentifier(processSpecificationId);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // Serializes derivations of one specification while allowing other specifications to proceed.
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@key));";
                lockCommand.Parameters.AddWithValue("key", normalizedId);
                await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            ProcessSpecification? baseline;
            await using (var baselineCommand = connection.CreateCommand())
            {
                baselineCommand.Transaction = transaction;
                baselineCommand.CommandText = """
                    SELECT payload::text
                    FROM process_specification_versions
                    WHERE process_specification_id = @key AND version = @version AND status = @published
                    FOR SHARE;
                    """;
                baselineCommand.Parameters.AddWithValue("key", normalizedId);
                baselineCommand.Parameters.AddWithValue("version", baseVersion);
                baselineCommand.Parameters.AddWithValue("published", ConfigurationStatuses.Published);
                var payload = await baselineCommand.ExecuteScalarAsync(ct).ConfigureAwait(false);
                baseline = payload is null or DBNull
                    ? null
                    : JsonSerializer.Deserialize<ProcessSpecification>((string)payload, JsonOptions);
            }
            if (baseline is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return new ProcessSpecificationDraftCreationResult { Conflict = "baseline-not-published" };
            }

            await using (var siblingCommand = connection.CreateCommand())
            {
                siblingCommand.Transaction = transaction;
                siblingCommand.CommandText = """
                    SELECT 1
                    FROM process_specification_versions
                    WHERE process_specification_id = @key
                      AND status = @draft
                      AND payload ->> 'basedOnVersion' = @base_version
                    LIMIT 1;
                    """;
                siblingCommand.Parameters.AddWithValue("key", normalizedId);
                siblingCommand.Parameters.AddWithValue("draft", ConfigurationStatuses.Draft);
                siblingCommand.Parameters.AddWithValue("base_version", baseVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (await siblingCommand.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return new ProcessSpecificationDraftCreationResult { Conflict = "draft-already-exists" };
                }
            }

            var nextVersion = 1;
            await using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) + 1 FROM process_specification_versions WHERE process_specification_id = @key;";
                versionCommand.Parameters.AddWithValue("key", normalizedId);
                nextVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            }

            var draft = baseline with
            {
                Version = nextVersion,
                BasedOnVersion = baseVersion,
                Status = ConfigurationStatuses.Draft,
                Values = MergeValues(baseline.Values, request.ParameterOverrides),
                ChangeReason = request.ChangeReason,
                MechanismNotes = request.MechanismNotes,
                EvidenceReferences = request.EvidenceReferences,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await using (var insertCommand = connection.CreateCommand())
            {
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO process_specification_versions(
                        process_specification_id, version, data_model_id, data_model_version, status, payload, updated_at)
                    VALUES (@key, @version, @model_id, @model_version, @status, @payload, @updated_at);
                    """;
                insertCommand.Parameters.AddWithValue("key", draft.ProcessSpecificationId);
                insertCommand.Parameters.AddWithValue("version", draft.Version);
                insertCommand.Parameters.AddWithValue("model_id", draft.DataModelId);
                insertCommand.Parameters.AddWithValue("model_version", draft.DataModelVersion);
                insertCommand.Parameters.AddWithValue("status", draft.Status);
                insertCommand.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft, JsonOptions));
                insertCommand.Parameters.AddWithValue("updated_at", draft.UpdatedAt.UtcDateTime);
                await insertCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new ProcessSpecificationDraftCreationResult { Draft = draft };
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return new ProcessSpecificationDraftCreationResult { Conflict = "version-conflict" };
        }
    }

    public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
        => DeleteAsync("process_specification_versions", "process_specification_id", processSpecificationId, version, ct);

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

    public Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default)
        => UpsertAsync(
            "scenario_packages", "package_id", value.PackageId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
        => ListAsync<ScenarioPackage>("scenario_packages", "ORDER BY package_id, version DESC", ct);

    public Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
        => GetAsync<ScenarioPackage>("scenario_packages", "package_id", packageId, version, ct);

    public Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
        => DeleteAsync("scenario_packages", "package_id", packageId, version, ct);

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
        command.Parameters.AddWithValue("key", NormalizeIdentifier(key));
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("status", status);
        if (hasModel)
        {
            command.Parameters.AddWithValue("model_id", NormalizeIdentifier(modelId!));
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
        command.Parameters.AddWithValue("key", NormalizeIdentifier(key));
        command.Parameters.AddWithValue("version", version);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull ? default : JsonSerializer.Deserialize<T>((string)payload, JsonOptions);
    }

    private async Task<bool> DeleteAsync(string table, string keyColumn, string key, int version, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE {keyColumn} = @key AND version = @version;");
        command.Parameters.AddWithValue("key", NormalizeIdentifier(key));
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static string NormalizeIdentifier(string value)
        => value.Trim().ToLowerInvariant();

    private static IReadOnlyList<ControlParameterValue> MergeValues(
        IReadOnlyList<ControlParameterValue> baseline,
        IReadOnlyList<ControlParameterValue> overrides)
    {
        var values = baseline.ToDictionary(item => item.Code, StringComparer.Ordinal);
        foreach (var item in overrides)
            values[item.Code] = item;
        return values.Values.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
    }
}
