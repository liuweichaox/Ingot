
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

    public async Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
        => RequireApplied(await TryUpsertDataModelAsync(value, ct).ConfigureAwait(false));

    public Task<ProcessConfigurationMutationResult<ProcessDataModel>> TryUpsertDataModelAsync(
        ProcessDataModel value,
        CancellationToken ct = default)
        => TryUpsertAsync(
            "process_data_models", "model_id", value.ModelId, value.Version, value.Status,
            null, null, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
        => ListAsync<ProcessDataModel>("process_data_models", "ORDER BY model_id, version DESC", ct);

    public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
        => GetAsync<ProcessDataModel>("process_data_models", "model_id", modelId, version, ct);

    public async Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default)
        => (await TryDeleteDataModelAsync(modelId, version, ct).ConfigureAwait(false)).Succeeded;

    public async Task<ProcessConfigurationDeleteResult> TryDeleteDataModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var key = NormalizeIdentifier(modelId);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockDataModelReferenceAsync(connection, transaction, key, version, ct).ConfigureAwait(false);
        try
        {
            await using var command = new NpgsqlCommand(
                """
                DELETE FROM process_data_models model
                WHERE model.model_id = @key
                  AND model.version = @version
                  AND model.status = @draft
                  AND NOT EXISTS (
                    SELECT 1 FROM process_specification_versions specification
                    WHERE specification.data_model_id = model.model_id
                      AND specification.data_model_version = model.version)
                  AND NOT EXISTS (
                    SELECT 1 FROM process_analysis_plans plan
                    WHERE plan.data_model_id = model.model_id
                      AND plan.data_model_version = model.version)
                  AND NOT EXISTS (
                    SELECT 1 FROM scenario_packages package
                    WHERE package.data_model_id = model.model_id
                      AND package.data_model_version = model.version)
                  AND NOT EXISTS (
                    SELECT 1 FROM ingestion_tasks task
                    WHERE lower(task.payload ->> 'dataModelId') = model.model_id
                      AND task.payload ->> 'dataModelVersion' = @version_text)
                  AND NOT EXISTS (
                    SELECT 1 FROM ingestion_task_templates template
                    WHERE lower(template.payload ->> 'dataModelId') = model.model_id
                      AND template.payload ->> 'dataModelVersion' = @version_text);
                """, connection, transaction);
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("version_text", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("draft", ConfigurationStatuses.Draft);
            var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
            if (deleted)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return ProcessConfigurationDeleteResult.Applied();
            }

            var status = await ReadStatusAsync(connection, transaction, "process_data_models", "model_id", key, version, ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return status is null
                ? ProcessConfigurationDeleteResult.NotFound()
                : status == ConfigurationStatuses.Draft
                    ? ProcessConfigurationDeleteResult.Referenced()
                    : ProcessConfigurationDeleteResult.StateConflict(status);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return ProcessConfigurationDeleteResult.Referenced();
        }
    }

    public async Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
        => RequireApplied(await TryUpsertProcessSpecificationAsync(value, ct).ConfigureAwait(false));

    public Task<ProcessConfigurationMutationResult<ProcessSpecification>> TryUpsertProcessSpecificationAsync(
        ProcessSpecification value,
        CancellationToken ct = default)
        => TryUpsertAsync(
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

    public async Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
        => (await TryDeleteProcessSpecificationAsync(processSpecificationId, version, ct).ConfigureAwait(false)).Succeeded;

    public Task<ProcessConfigurationDeleteResult> TryDeleteProcessSpecificationAsync(
        string processSpecificationId,
        int version,
        CancellationToken ct = default)
        => TryDeleteAsync("process_specification_versions", "process_specification_id", processSpecificationId, version, ct);

    public async Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
        => RequireApplied(await TryUpsertAnalysisPlanAsync(value, ct).ConfigureAwait(false));

    public Task<ProcessConfigurationMutationResult<ProcessAnalysisPlan>> TryUpsertAnalysisPlanAsync(
        ProcessAnalysisPlan value,
        CancellationToken ct = default)
        => TryUpsertAsync(
            "process_analysis_plans", "plan_id", value.PlanId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct);

    public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
        => ListAsync<ProcessAnalysisPlan>("process_analysis_plans", "ORDER BY plan_id, version DESC", ct);

    public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
        => GetAsync<ProcessAnalysisPlan>("process_analysis_plans", "plan_id", planId, version, ct);

    public async Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
        => (await TryDeleteAnalysisPlanAsync(planId, version, ct).ConfigureAwait(false)).Succeeded;

    public Task<ProcessConfigurationDeleteResult> TryDeleteAnalysisPlanAsync(
        string planId,
        int version,
        CancellationToken ct = default)
        => TryDeleteAsync("process_analysis_plans", "plan_id", planId, version, ct);

    public async Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default)
        => RequireApplied(await TryUpsertScenarioPackageAsync(value, ct).ConfigureAwait(false));

    public Task<ProcessConfigurationMutationResult<ScenarioPackage>> TryUpsertScenarioPackageAsync(
        ScenarioPackage value,
        CancellationToken ct = default)
        => TryUpsertAsync(
            "scenario_packages", "package_id", value.PackageId, value.Version, value.Status,
            value.DataModelId, value.DataModelVersion, value, value.UpdatedAt, ct,
            value.AnalysisPlanId, value.AnalysisPlanVersion);

    public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
        => ListAsync<ScenarioPackage>("scenario_packages", "ORDER BY package_id, version DESC", ct);

    public Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
        => GetAsync<ScenarioPackage>("scenario_packages", "package_id", packageId, version, ct);

    public async Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
        => (await TryDeleteScenarioPackageAsync(packageId, version, ct).ConfigureAwait(false)).Succeeded;

    public Task<ProcessConfigurationDeleteResult> TryDeleteScenarioPackageAsync(
        string packageId,
        int version,
        CancellationToken ct = default)
        => TryDeleteAsync("scenario_packages", "package_id", packageId, version, ct);

    private async Task<ProcessConfigurationMutationResult<T>> TryUpsertAsync<T>(
        string table,
        string keyColumn,
        string key,
        int version,
        string status,
        string? modelId,
        int? modelVersion,
        T payload,
        DateTimeOffset updatedAt,
        CancellationToken ct,
        string? analysisPlanId = null,
        int? analysisPlanVersion = null)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var hasModel = modelId is not null && modelVersion.HasValue;
        var hasAnalysisPlan = analysisPlanId is not null && analysisPlanVersion.HasValue;
        var columns = hasAnalysisPlan
            ? $"{keyColumn}, version, data_model_id, data_model_version, analysis_plan_id, analysis_plan_version, status, payload, updated_at"
            : hasModel
            ? $"{keyColumn}, version, data_model_id, data_model_version, status, payload, updated_at"
            : $"{keyColumn}, version, status, payload, updated_at";
        var values = hasAnalysisPlan
            ? "@key, @version, @model_id, @model_version, @analysis_plan_id, @analysis_plan_version, @status, @payload, @updated_at"
            : hasModel
            ? "@key, @version, @model_id, @model_version, @status, @payload, @updated_at"
            : "@key, @version, @status, @payload, @updated_at";
        var updates = hasAnalysisPlan
            ? "data_model_id = EXCLUDED.data_model_id, data_model_version = EXCLUDED.data_model_version, analysis_plan_id = EXCLUDED.analysis_plan_id, analysis_plan_version = EXCLUDED.analysis_plan_version, status = EXCLUDED.status, payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at"
            : hasModel
            ? "data_model_id = EXCLUDED.data_model_id, data_model_version = EXCLUDED.data_model_version, status = EXCLUDED.status, payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at"
            : "status = EXCLUDED.status, payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at";
        await using var command = _dataSource.CreateCommand(
            $"""
            INSERT INTO {table}({columns}) VALUES ({values})
            ON CONFLICT ({keyColumn}, version) DO UPDATE SET {updates}
            WHERE {table}.status = @draft
               OR ({table}.status = @published AND EXCLUDED.status = @retired)
            RETURNING payload::text;
            """);
        command.Parameters.AddWithValue("key", NormalizeIdentifier(key));
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("status", status);
        if (hasModel)
        {
            command.Parameters.AddWithValue("model_id", NormalizeIdentifier(modelId!));
            command.Parameters.AddWithValue("model_version", modelVersion!.Value);
        }
        if (hasAnalysisPlan)
        {
            command.Parameters.AddWithValue("analysis_plan_id", NormalizeIdentifier(analysisPlanId!));
            command.Parameters.AddWithValue("analysis_plan_version", analysisPlanVersion!.Value);
        }
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, JsonOptions));
        command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
        command.Parameters.AddWithValue("draft", ConfigurationStatuses.Draft);
        command.Parameters.AddWithValue("published", ConfigurationStatuses.Published);
        command.Parameters.AddWithValue("retired", ConfigurationStatuses.Retired);
        object? written;
        try
        {
            written = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ProcessConfigurationMutationResult<T>.StateConflict(
                await GetAsync<T>(table, keyColumn, key, version, ct).ConfigureAwait(false));
        }
        if (written is string stored)
            return ProcessConfigurationMutationResult<T>.Applied(
                JsonSerializer.Deserialize<T>(stored, JsonOptions)!);
        return ProcessConfigurationMutationResult<T>.StateConflict(
            await GetAsync<T>(table, keyColumn, key, version, ct).ConfigureAwait(false));
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

    private async Task<ProcessConfigurationDeleteResult> TryDeleteAsync(
        string table,
        string keyColumn,
        string key,
        int version,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var normalizedKey = NormalizeIdentifier(key);
        await using var command = _dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE {keyColumn} = @key AND version = @version AND status = @draft;");
        command.Parameters.AddWithValue("key", normalizedKey);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("draft", ConfigurationStatuses.Draft);
        try
        {
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
                return ProcessConfigurationDeleteResult.Applied();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ProcessConfigurationDeleteResult.Referenced();
        }
        var existingStatus = await GetStatusAsync(table, keyColumn, normalizedKey, version, ct).ConfigureAwait(false);
        return existingStatus is null
            ? ProcessConfigurationDeleteResult.NotFound()
            : ProcessConfigurationDeleteResult.StateConflict(existingStatus);
    }

    private static T RequireApplied<T>(ProcessConfigurationMutationResult<T> result)
        => result.Succeeded
            ? result.Value!
            : throw new InvalidOperationException("工艺配置版本已发生并发状态变化。");

    private static async Task LockDataModelReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string modelId,
        int version,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));", connection, transaction);
        command.Parameters.AddWithValue("key", $"process-data-model:{modelId}@{version}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string keyColumn,
        string key,
        int version,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT status FROM {table} WHERE {keyColumn} = @key AND version = @version;", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private async Task<string?> GetStatusAsync(
        string table,
        string keyColumn,
        string key,
        int version,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            $"SELECT status FROM {table} WHERE {keyColumn} = @key AND version = @version;");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
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
