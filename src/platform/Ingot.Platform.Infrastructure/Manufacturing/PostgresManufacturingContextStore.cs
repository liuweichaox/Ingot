
using System.Text.Json;
using Ingot.Contracts.Manufacturing;
using Ingot.Platform.Application.Manufacturing;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Manufacturing;

public sealed class PostgresManufacturingContextStore : IManufacturingContextStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresManufacturingContextStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<ToolingTypeDefinition> CreateToolingTypeAsync(
        ToolingTypeDefinition value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var acceptedComponentTypes = value.Roles
            .SelectMany(static role => role.AcceptedComponentTypeCodes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var componentTypeCode in acceptedComponentTypes)
        {
            var componentType = await GetComponentTypeAsync(componentTypeCode, ct).ConfigureAwait(false);
            if (componentType is null)
                throw new InvalidOperationException($"组件类型 {componentTypeCode} 不存在，请先在组件类型中配置。");
            if (componentType.Status != "active")
                throw new InvalidOperationException($"组件类型 {componentTypeCode} 已停用，不能用于新的工装类型版本。");
        }
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_types(tooling_type_code, version, payload, updated_at)
            VALUES (@code, @version, @payload, @updated_at)
            ON CONFLICT (tooling_type_code, version) DO NOTHING;
            """);
        command.Parameters.AddWithValue("code", value.ToolingTypeCode);
        command.Parameters.AddWithValue("version", value.Version);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("该工装类型版本已存在；已发布版本不可原地修改，请创建新版本。");
        return value;
    }

    public Task<IReadOnlyList<ToolingTypeDefinition>> ListToolingTypesAsync(CancellationToken ct = default)
        => ListAsync<ToolingTypeDefinition>(
            "SELECT payload::text FROM tooling_types ORDER BY tooling_type_code, version DESC;", null, ct);

    public async Task<bool> DeleteToolingTypeAsync(
        string toolingTypeCode,
        int version,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM tooling_assemblies WHERE tooling_type_code = @code LIMIT 1;",
                command => command.Parameters.AddWithValue("code", toolingTypeCode), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该工装类型已被工装组合引用，不能删除；请将该版本停用。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_types WHERE tooling_type_code = @code AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("code", toolingTypeCode);
                command.Parameters.AddWithValue("version", version);
            }, ct).ConfigureAwait(false);
    }

    public async Task<ToolingComponentTypeDefinition> UpsertComponentTypeAsync(
        ToolingComponentTypeDefinition value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_component_types(component_type_code, payload, updated_at)
            VALUES (@code, @payload, @updated_at)
            ON CONFLICT (component_type_code) DO UPDATE SET
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("code", value.ComponentTypeCode);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ToolingComponentTypeDefinition>> ListComponentTypesAsync(CancellationToken ct = default)
        => ListAsync<ToolingComponentTypeDefinition>(
            "SELECT payload::text FROM tooling_component_types ORDER BY component_type_code;", null, ct);

    public async Task<bool> DeleteComponentTypeAsync(string componentTypeCode, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var referenced = await ExistsAsync(
            """
            SELECT 1 FROM tooling_components WHERE component_type_code = @code
            UNION ALL
            SELECT 1
            FROM tooling_types value
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(value.payload->'roles', '[]'::jsonb)) role
            WHERE jsonb_exists(COALESCE(role->'acceptedComponentTypeCodes', '[]'::jsonb), @code)
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("code", componentTypeCode), ct).ConfigureAwait(false);
        if (referenced)
            throw new InvalidOperationException("该组件类型已被组件或工装类型引用，不能删除；请先解除引用或停用。");
        return await DeleteAsync(
            "DELETE FROM tooling_component_types WHERE component_type_code = @code;",
            command => command.Parameters.AddWithValue("code", componentTypeCode), ct).ConfigureAwait(false);
    }

    public async Task<ToolingComponent> UpsertComponentAsync(ToolingComponent value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var existing = await GetComponentAsync(value.ComponentId, ct).ConfigureAwait(false);
        if (existing is not null &&
            (existing.ComponentTypeCode != value.ComponentTypeCode ||
             existing.SerialNo != value.SerialNo))
        {
            throw new InvalidOperationException(
                "已登记组件的组件类型和序列号不可修改；请新建组件身份。");
        }
        var componentType = await GetComponentTypeAsync(value.ComponentTypeCode, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("组件类型不存在，请先在组件类型中配置。");
        if (componentType.Status != "active")
            throw new InvalidOperationException("组件类型已停用，不能登记新的组件。");
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_components(
              component_id, component_type_code, serial_no, payload, updated_at)
            VALUES (@id, @component_type, @serial, @payload, @updated_at)
            ON CONFLICT (component_id) DO UPDATE SET
              component_type_code = EXCLUDED.component_type_code,
              serial_no = EXCLUDED.serial_no,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.ComponentId);
        command.Parameters.AddWithValue("component_type", value.ComponentTypeCode);
        command.Parameters.AddWithValue("serial", value.SerialNo);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ToolingComponent>> ListComponentsAsync(
        string? componentTypeCode = null,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM tooling_components " +
                           "WHERE (@type = '' OR component_type_code = @type) ORDER BY component_type_code, component_id;";
        return ListAsync<ToolingComponent>(sql,
            command => command.Parameters.AddWithValue("type", componentTypeCode?.Trim().ToLowerInvariant() ?? ""), ct);
    }

    public async Task<bool> DeleteComponentAsync(string componentId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                """
                SELECT 1
                FROM tooling_assembly_revisions value
                CROSS JOIN LATERAL jsonb_array_elements(COALESCE(value.payload->'members', '[]'::jsonb)) member
                WHERE member->>'componentId' = @id
                LIMIT 1;
                """,
                command => command.Parameters.AddWithValue("id", componentId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该组件已进入工装组合历史，不能删除；请将组件退役。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_components WHERE component_id = @id;",
            command => command.Parameters.AddWithValue("id", componentId), ct).ConfigureAwait(false);
    }

    public async Task<ToolingAssembly> UpsertAssemblyAsync(ToolingAssembly value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var existing = await GetAssemblyAsync(value.ToolingAssemblyId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ToolingTypeCode != value.ToolingTypeCode)
            throw new InvalidOperationException("已创建工装总成的工装类型不可修改；请新建工装总成编号。");
        _ = await GetLatestToolingTypeAsync(value.ToolingTypeCode, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("工装类型不存在。");
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_assemblies(tooling_assembly_id, tooling_type_code, payload, updated_at)
            VALUES (@id, @type, @payload, @updated_at)
            ON CONFLICT (tooling_assembly_id) DO UPDATE SET
              tooling_type_code = EXCLUDED.tooling_type_code,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.ToolingAssemblyId);
        command.Parameters.AddWithValue("type", value.ToolingTypeCode);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ToolingAssembly>> ListAssembliesAsync(CancellationToken ct = default)
        => ListAsync<ToolingAssembly>(
            "SELECT payload::text FROM tooling_assemblies ORDER BY tooling_assembly_id;", null, ct);

    public async Task<bool> DeleteAssemblyAsync(string toolingAssemblyId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM tooling_assembly_revisions WHERE tooling_assembly_id = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", toolingAssemblyId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该工装已存在组合版本，不能删除；请将工装停用。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_assemblies WHERE tooling_assembly_id = @id;",
            command => command.Parameters.AddWithValue("id", toolingAssemblyId), ct).ConfigureAwait(false);
    }

    public async Task<ToolingAssemblyRevision> CreateAssemblyRevisionAsync(
        ToolingAssemblyRevision value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var assembly = await GetAssemblyAsync(value.ToolingAssemblyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("工装总成编号不存在。");
        var type = await GetToolingTypeAsync(assembly.ToolingTypeCode, value.ToolingTypeVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("工装总成对应的工装结构版本不存在。");
        if (!string.Equals(type.Status, "active", StringComparison.Ordinal))
            throw new InvalidOperationException("工装结构版本已停用，不能创建新的工装总成版本。");
        await ValidateAssemblyMembersAsync(value, type, ct).ConfigureAwait(false);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireAdvisoryLockAsync(connection, transaction, $"assembly:{value.ToolingAssemblyId}", ct).ConfigureAwait(false);
        var revision = value with
        {
            Revision = await NextAssemblyRevisionAsync(connection, transaction, value.ToolingAssemblyId, ct).ConfigureAwait(false)
        };
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tooling_assembly_revisions(
              assembly_revision_id, tooling_assembly_id, revision, payload, created_at)
            VALUES (@id, @tooling_assembly_id, @revision, @payload, @created_at)
            ON CONFLICT DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", revision.AssemblyRevisionId);
        command.Parameters.AddWithValue("tooling_assembly_id", revision.ToolingAssemblyId);
        command.Parameters.AddWithValue("revision", revision.Revision);
        AddJson(command, "payload", revision);
        command.Parameters.AddWithValue("created_at", revision.CreatedAt.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("该工装总成版本已存在；组合版本不可修改，请创建下一版本。");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return revision;
    }

    public Task<IReadOnlyList<ToolingAssemblyRevision>> ListAssemblyRevisionsAsync(
        string? toolingAssemblyId = null,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM tooling_assembly_revisions " +
                           "WHERE (@tooling_assembly_id = '' OR tooling_assembly_id = @tooling_assembly_id) ORDER BY tooling_assembly_id, revision DESC;";
        return ListAsync<ToolingAssemblyRevision>(sql,
            command => command.Parameters.AddWithValue("tooling_assembly_id", toolingAssemblyId?.Trim() ?? ""), ct);
    }

    public async Task<bool> DeleteAssemblyRevisionAsync(Guid assemblyRevisionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM tooling_installations WHERE assembly_revision_id = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", assemblyRevisionId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该组合版本已有工装装卸记录，不能删除。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_assembly_revisions WHERE assembly_revision_id = @id;",
            command => command.Parameters.AddWithValue("id", assemblyRevisionId), ct).ConfigureAwait(false);
    }

    public async Task<ToolingInstallation> CreateInstallationAsync(
        ToolingInstallation value,
        CancellationToken ct = default)
        => await ReplaceInstallationAsync(value, ct).ConfigureAwait(false);

    public async Task<ToolingInstallation> ReplaceInstallationAsync(
        ToolingInstallation value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (value.RemovedAt.HasValue)
            throw new InvalidOperationException("工装替换只能创建当前有效的装入记录。");
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await AcquireAdvisoryLockAsync(connection, transaction, $"equipment:{value.SiteId}:{value.EquipmentId}", ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(value.CommandId))
        {
            await AcquireAdvisoryLockAsync(connection, transaction, $"installation-command:{value.CommandId}", ct)
                .ConfigureAwait(false);
            var existing = await GetByCommandIdAsync(connection, transaction, value.CommandId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!SameInstallationCommand(existing, value))
                    throw new InvalidOperationException("CommandId 已用于另一条工装替换，不能重复表示不同业务操作。");
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return existing;
            }
        }

        var active = await GetActiveInstallationForEquipmentAsync(connection, transaction, value.SiteId, value.EquipmentId, ct)
            .ConfigureAwait(false);
        if (active is not null)
        {
            if (value.InstalledAt <= active.InstalledAt)
                throw new InvalidOperationException("新的工装装入时间必须晚于当前装入记录。");
            await CloseInstallationAsync(connection, transaction, active, value.InstalledAt, value.Actor, ct)
                .ConfigureAwait(false);
        }

        await EnsureRevisionInstallableAsync(connection, transaction, value.AssemblyRevisionId, ct).ConfigureAwait(false);
        await EnsureNoInstallationOverlapAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tooling_installations(
              installation_id, site_id, equipment_id, assembly_revision_id, installed_at, removed_at,
              source, command_id, payload, created_at)
            VALUES (@id, @site, @equipment, @revision, @installed_at, @removed_at,
                    @source, @command_id, @payload, @created_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.InstallationId);
        command.Parameters.AddWithValue("site", value.SiteId);
        command.Parameters.AddWithValue("equipment", value.EquipmentId);
        command.Parameters.AddWithValue("revision", value.AssemblyRevisionId);
        command.Parameters.AddWithValue("installed_at", value.InstalledAt.UtcDateTime);
        command.Parameters.AddWithValue("removed_at", (object?)value.RemovedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("source", value.Source);
        command.Parameters.AddWithValue("command_id", (object?)value.CommandId ?? DBNull.Value);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<ToolingInstallation?> RemoveInstallationAsync(
        string siteId,
        Guid installationId,
        DateTimeOffset removedAt,
        string? actor,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var existing = await GetInstallationAsync(connection, transaction, siteId, installationId, ct).ConfigureAwait(false);
        if (existing is null)
            return null;
        if (existing.RemovedAt.HasValue)
            return existing;
        await AcquireAdvisoryLockAsync(connection, transaction, $"equipment:{existing.SiteId}:{existing.EquipmentId}", ct)
            .ConfigureAwait(false);
        var updated = await CloseInstallationAsync(
            connection,
            transaction,
            existing,
            removedAt.ToUniversalTime(),
            actor,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return updated;
    }

    public Task<IReadOnlyList<ToolingInstallation>> ListInstallationsAsync(
        string? siteId = null,
        string? equipmentId = null,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM tooling_installations " +
                           "WHERE (@site = '' OR site_id = @site) " +
                           "AND (@equipment = '' OR equipment_id = @equipment) " +
                           "AND (NOT @active OR removed_at IS NULL) ORDER BY installed_at DESC;";
        return ListAsync<ToolingInstallation>(sql, command =>
        {
            command.Parameters.AddWithValue("site", siteId?.Trim() ?? "");
            command.Parameters.AddWithValue("equipment", equipmentId?.Trim() ?? "");
            command.Parameters.AddWithValue("active", activeOnly);
        }, ct);
    }

    public async Task<bool> DeleteInstallationAsync(string siteId, Guid installationId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM production_contexts WHERE tooling_installation_id = @id AND site_id = @site LIMIT 1;",
                command =>
                {
                    command.Parameters.AddWithValue("id", installationId);
                    command.Parameters.AddWithValue("site", siteId);
                }, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该工装装卸记录已被生产配置引用，不能删除；错误记录应先结束并保留追溯。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_installations WHERE installation_id = @id AND site_id = @site;",
            command =>
            {
                command.Parameters.AddWithValue("id", installationId);
                command.Parameters.AddWithValue("site", siteId);
            }, ct).ConfigureAwait(false);
    }

    public async Task<ProductionContext> StartProductionContextAsync(
        ProductionContext value,
        CancellationToken ct = default)
        => await ReplaceProductionContextAsync(value, ct).ConfigureAwait(false);

    public async Task<ProductionContext> ReplaceProductionContextAsync(
        ProductionContext value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (value.ValidTo.HasValue)
            throw new InvalidOperationException("生产切换只能创建当前有效的生产上下文。");
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireAdvisoryLockAsync(connection, transaction, $"equipment:{value.SiteId}:{value.EquipmentId}", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(value.CommandId))
        {
            await AcquireAdvisoryLockAsync(connection, transaction, $"production-command:{value.CommandId}", ct)
                .ConfigureAwait(false);
            var existingCommand = await GetProductionContextByCommandIdAsync(
                connection, transaction, value.CommandId, ct).ConfigureAwait(false);
            if (existingCommand is not null)
            {
                if (!SameProductionCommand(existingCommand, value))
                    throw new InvalidOperationException("CommandId 已用于另一条生产上下文，不能重复表示不同业务操作。");
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return existingCommand;
            }
        }
        await EnsureInstallationMatchesAsync(connection, transaction, value, ct).ConfigureAwait(false);

        var active = await GetActiveProductionContextForEquipmentAsync(connection, transaction, value.SiteId, value.EquipmentId, ct)
            .ConfigureAwait(false);
        if (active is not null)
        {
            if (value.ValidFrom <= active.ValidFrom)
                throw new InvalidOperationException("新的生产上下文生效时间必须晚于当前上下文开始时间。");
            if (string.Equals(active.Source, "mes", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value.Source, "manual", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该设备当前生产信息由 MES 管理，不能在平台重复人工录入。");
            }
            await CloseProductionContextAsync(connection, transaction, active, value.ValidFrom, value.Actor, ct)
                .ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO production_contexts(
              context_id, site_id, equipment_id, tooling_installation_id, valid_from, valid_to, source, command_id, payload, updated_at)
            VALUES (@id, @site, @equipment, @installation, @valid_from, @valid_to, @source, @command_id, @payload, @updated_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.ContextId);
        command.Parameters.AddWithValue("site", value.SiteId);
        command.Parameters.AddWithValue("equipment", value.EquipmentId);
        command.Parameters.AddWithValue("installation", value.ToolingInstallationId);
        command.Parameters.AddWithValue("valid_from", value.ValidFrom.UtcDateTime);
        command.Parameters.AddWithValue("valid_to", (object?)value.ValidTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("source", value.Source);
        command.Parameters.AddWithValue("command_id", (object?)value.CommandId ?? DBNull.Value);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<ProductionContext?> CloseProductionContextAsync(
        string siteId,
        Guid contextId,
        DateTimeOffset validTo,
        string? actor,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var existing = await GetProductionContextForUpdateAsync(connection, transaction, siteId, contextId, ct).ConfigureAwait(false);
        if (existing is null)
            return null;
        if (existing.ValidTo.HasValue)
            return existing;
        await AcquireAdvisoryLockAsync(connection, transaction, $"equipment:{existing.SiteId}:{existing.EquipmentId}", ct)
            .ConfigureAwait(false);
        var updated = await CloseProductionContextAsync(
            connection,
            transaction,
            existing,
            validTo.ToUniversalTime(),
            actor,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return updated;
    }

    public Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(
        string? siteId = null,
        string? equipmentId = null,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM production_contexts " +
                           "WHERE (@site = '' OR site_id = @site) " +
                           "AND (@equipment = '' OR equipment_id = @equipment) " +
                           "AND (NOT @active OR valid_to IS NULL) ORDER BY valid_from DESC;";
        return ListAsync<ProductionContext>(sql, command =>
        {
            command.Parameters.AddWithValue("site", siteId?.Trim() ?? "");
            command.Parameters.AddWithValue("equipment", equipmentId?.Trim() ?? "");
            command.Parameters.AddWithValue("active", activeOnly);
        }, ct);
    }

    public async Task<bool> DeleteProductionContextAsync(string siteId, Guid contextId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM production_events WHERE site_id = @site AND context->>'production_context_id' = @id LIMIT 1;",
                command =>
                {
                    command.Parameters.AddWithValue("site", siteId);
                    command.Parameters.AddWithValue("id", contextId.ToString("D"));
                }, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该生产配置已被生产事件固化引用，不能删除；请结束其生效区间。");
        }
        return await DeleteAsync(
            "DELETE FROM production_contexts WHERE context_id = @id AND site_id = @site;",
            command =>
            {
                command.Parameters.AddWithValue("id", contextId);
                command.Parameters.AddWithValue("site", siteId);
            }, ct).ConfigureAwait(false);
    }

    public async Task<ResolvedProductionContext?> ResolveAsync(
        string siteId,
        string equipmentId,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT pc.payload::text, ti.payload::text, ar.payload::text, a.payload::text
            FROM production_contexts pc
            JOIN tooling_installations ti ON ti.installation_id = pc.tooling_installation_id
            JOIN tooling_assembly_revisions ar ON ar.assembly_revision_id = ti.assembly_revision_id
            JOIN tooling_assemblies a ON a.tooling_assembly_id = ar.tooling_assembly_id
            WHERE pc.site_id = @site AND pc.equipment_id = @equipment
              AND pc.valid_from <= @at AND (pc.valid_to IS NULL OR pc.valid_to > @at)
              AND ti.installed_at <= @at AND (ti.removed_at IS NULL OR ti.removed_at > @at)
            ORDER BY pc.valid_from DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("site", siteId.Trim());
        command.Parameters.AddWithValue("equipment", equipmentId.Trim());
        command.Parameters.AddWithValue("at", at.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new ResolvedProductionContext
        {
            Production = Deserialize<ProductionContext>(reader.GetString(0)),
            Installation = Deserialize<ToolingInstallation>(reader.GetString(1)),
            AssemblyRevision = Deserialize<ToolingAssemblyRevision>(reader.GetString(2)),
            Assembly = Deserialize<ToolingAssembly>(reader.GetString(3))
        };
    }

    private async Task ValidateAssemblyMembersAsync(
        ToolingAssemblyRevision revision,
        ToolingTypeDefinition type,
        CancellationToken ct)
    {
        var allowed = type.Roles.ToDictionary(static role => role.Code, StringComparer.Ordinal);
        var missing = type.Roles.Where(static role => role.Required)
            .Select(static role => role.Code)
            .Except(revision.Members.Select(static member => member.RoleCode), StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"组合缺少必需角色：{string.Join("、", missing)}。");
        foreach (var member in revision.Members)
        {
            if (!allowed.TryGetValue(member.RoleCode, out var role))
                throw new InvalidOperationException($"角色 {member.RoleCode} 不属于该工装类型。");
            var component = await GetComponentAsync(member.ComponentId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"组件 {member.ComponentId} 不存在。");
            if (role.AcceptedComponentTypeCodes.Count > 0 &&
                !role.AcceptedComponentTypeCodes.Contains(component.ComponentTypeCode, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"组件 {member.ComponentId} 的类型 {component.ComponentTypeCode} 不适用于角色 {member.RoleCode}。");
            }
        }
    }

    private async Task<ToolingTypeDefinition?> GetLatestToolingTypeAsync(string code, CancellationToken ct)
        => await GetAsync<ToolingTypeDefinition>(
            "SELECT payload::text FROM tooling_types WHERE tooling_type_code = @code ORDER BY version DESC LIMIT 1;",
            command => command.Parameters.AddWithValue("code", code), ct).ConfigureAwait(false);

    private async Task<ToolingTypeDefinition?> GetToolingTypeAsync(string code, int version, CancellationToken ct)
        => await GetAsync<ToolingTypeDefinition>(
            "SELECT payload::text FROM tooling_types WHERE tooling_type_code = @code AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("code", code);
                command.Parameters.AddWithValue("version", version);
            }, ct).ConfigureAwait(false);

    private async Task<ToolingComponentTypeDefinition?> GetComponentTypeAsync(string code, CancellationToken ct)
        => await GetAsync<ToolingComponentTypeDefinition>(
            "SELECT payload::text FROM tooling_component_types WHERE component_type_code = @code;",
            command => command.Parameters.AddWithValue("code", code), ct).ConfigureAwait(false);

    private async Task<ToolingComponent?> GetComponentAsync(string id, CancellationToken ct)
        => await GetAsync<ToolingComponent>(
            "SELECT payload::text FROM tooling_components WHERE component_id = @id;",
            command => command.Parameters.AddWithValue("id", id), ct).ConfigureAwait(false);

    private async Task<ToolingAssembly?> GetAssemblyAsync(string id, CancellationToken ct)
        => await GetAsync<ToolingAssembly>(
            "SELECT payload::text FROM tooling_assemblies WHERE tooling_assembly_id = @id;",
            command => command.Parameters.AddWithValue("id", id), ct).ConfigureAwait(false);

    private static async Task<ToolingInstallation?> GetInstallationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        Guid id,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM tooling_installations WHERE installation_id = @id AND site_id = @site FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("site", siteId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ToolingInstallation>((string)value);
    }

    private static async Task<ToolingInstallation?> GetActiveInstallationForEquipmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string equipmentId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT payload::text FROM tooling_installations
            WHERE site_id = @site AND equipment_id = @equipment AND removed_at IS NULL
            LIMIT 1 FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("site", siteId);
        command.Parameters.AddWithValue("equipment", equipmentId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ToolingInstallation>((string)value);
    }

    private static async Task<ProductionContext?> GetActiveProductionContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid installationId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT payload::text FROM production_contexts
            WHERE tooling_installation_id = @id AND valid_to IS NULL
            LIMIT 1 FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", installationId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ProductionContext>((string)value);
    }

    private static async Task<ProductionContext?> GetActiveProductionContextForEquipmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string equipmentId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT payload::text FROM production_contexts
            WHERE site_id = @site AND equipment_id = @equipment AND valid_to IS NULL
            LIMIT 1 FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("site", siteId);
        command.Parameters.AddWithValue("equipment", equipmentId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ProductionContext>((string)value);
    }

    private static async Task<ProductionContext?> GetProductionContextForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        Guid contextId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM production_contexts WHERE context_id = @id AND site_id = @site FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", contextId);
        command.Parameters.AddWithValue("site", siteId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ProductionContext>((string)value);
    }

    private async Task<ProductionContext?> GetProductionContextAsync(string siteId, Guid id, CancellationToken ct)
        => await GetAsync<ProductionContext>(
            "SELECT payload::text FROM production_contexts WHERE context_id = @id AND site_id = @site;",
            command =>
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("site", siteId);
            }, ct).ConfigureAwait(false);

    private static async Task<ProductionContext?> GetProductionContextByCommandIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM production_contexts WHERE command_id = @command_id FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("command_id", commandId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ProductionContext>((string)value);
    }

    private static bool SameProductionCommand(ProductionContext left, ProductionContext right)
        => string.Equals(left.SiteId, right.SiteId, StringComparison.Ordinal) &&
           string.Equals(left.EquipmentId, right.EquipmentId, StringComparison.Ordinal) &&
           string.Equals(left.ProductFamilyCode, right.ProductFamilyCode, StringComparison.Ordinal) &&
           string.Equals(left.ProductCode, right.ProductCode, StringComparison.Ordinal) &&
           string.Equals(left.ProcessSpecificationId, right.ProcessSpecificationId, StringComparison.Ordinal) &&
           string.Equals(left.ProcessSpecificationVersion, right.ProcessSpecificationVersion, StringComparison.Ordinal) &&
           left.ToolingInstallationId == right.ToolingInstallationId &&
           left.ValidFrom == right.ValidFrom &&
           string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
           string.Equals(left.ExternalOrderRef, right.ExternalOrderRef, StringComparison.Ordinal) &&
           string.Equals(left.ExternalBatchRef, right.ExternalBatchRef, StringComparison.Ordinal) &&
           string.Equals(left.MaterialLotRef, right.MaterialLotRef, StringComparison.Ordinal) &&
           string.Equals(left.MaterialSpecification, right.MaterialSpecification, StringComparison.Ordinal) &&
           string.Equals(left.MaintenanceStatus, right.MaintenanceStatus, StringComparison.Ordinal) &&
           string.Equals(left.CalibrationStatus, right.CalibrationStatus, StringComparison.Ordinal) &&
           string.Equals(left.CalibrationRef, right.CalibrationRef, StringComparison.Ordinal) &&
           left.CalibrationValidUntil == right.CalibrationValidUntil;

    private static bool SameInstallationCommand(ToolingInstallation left, ToolingInstallation right)
        => string.Equals(left.SiteId, right.SiteId, StringComparison.Ordinal) &&
           string.Equals(left.EquipmentId, right.EquipmentId, StringComparison.Ordinal) &&
           left.AssemblyRevisionId == right.AssemblyRevisionId &&
           left.InstalledAt == right.InstalledAt &&
           string.Equals(left.Source, right.Source, StringComparison.Ordinal);

    private static async Task<ToolingInstallation?> GetByCommandIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM tooling_installations WHERE command_id = @command_id;", connection, transaction);
        command.Parameters.AddWithValue("command_id", commandId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Deserialize<ToolingInstallation>((string)value);
    }

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> NextAssemblyRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string toolingAssemblyId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(revision), 0) + 1 FROM tooling_assembly_revisions WHERE tooling_assembly_id = @id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", toolingAssemblyId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    private static async Task<ToolingInstallation> CloseInstallationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolingInstallation existing,
        DateTimeOffset removedAt,
        string? actor,
        CancellationToken ct)
    {
        if (removedAt <= existing.InstalledAt)
            throw new InvalidOperationException("卸下时间必须晚于装入时间。");

        var activeContext = await GetActiveProductionContextAsync(connection, transaction, existing.InstallationId, ct)
            .ConfigureAwait(false);
        if (activeContext is not null)
            await CloseProductionContextAsync(connection, transaction, activeContext, removedAt, actor, ct).ConfigureAwait(false);

        var updated = existing with
        {
            RemovedAt = removedAt,
            Actor = actor?.Trim() ?? existing.Actor
        };
        await using var command = new NpgsqlCommand(
            """
            UPDATE tooling_installations
            SET removed_at = @removed_at, payload = @payload
            WHERE installation_id = @id AND removed_at IS NULL;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", existing.InstallationId);
        command.Parameters.AddWithValue("removed_at", updated.RemovedAt.Value.UtcDateTime);
        AddJson(command, "payload", updated);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("工装装入记录已被其他操作结束，请刷新后重试。");
        return updated;
    }

    private static async Task<ProductionContext> CloseProductionContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProductionContext existing,
        DateTimeOffset validTo,
        string? actor,
        CancellationToken ct)
    {
        if (validTo <= existing.ValidFrom)
            throw new InvalidOperationException("结束时间必须晚于生产上下文开始时间。");
        var updated = existing with
        {
            ValidTo = validTo,
            Actor = actor?.Trim() ?? existing.Actor,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var command = new NpgsqlCommand(
            """
            UPDATE production_contexts
            SET valid_to = @valid_to, payload = @payload, updated_at = @updated_at
            WHERE context_id = @id AND valid_to IS NULL;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", existing.ContextId);
        command.Parameters.AddWithValue("valid_to", updated.ValidTo.Value.UtcDateTime);
        AddJson(command, "payload", updated);
        command.Parameters.AddWithValue("updated_at", updated.UpdatedAt.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("生产上下文已被其他操作结束，请刷新后重试。");
        return updated;
    }

    private static async Task EnsureRevisionInstallableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid revisionId,
        CancellationToken ct)
    {
        ToolingAssemblyRevision revision;
        ToolingAssembly assembly;
        await using (var command = new NpgsqlCommand(
            """
            SELECT revision.payload::text, assembly.payload::text
            FROM tooling_assembly_revisions revision
            JOIN tooling_assemblies assembly ON assembly.tooling_assembly_id = revision.tooling_assembly_id
            WHERE revision.assembly_revision_id = @id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", revisionId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("工装组合版本不存在。");
            revision = Deserialize<ToolingAssemblyRevision>(reader.GetString(0));
            assembly = Deserialize<ToolingAssembly>(reader.GetString(1));
        }

        if (!string.Equals(assembly.Status, "active", StringComparison.Ordinal))
            throw new InvalidOperationException("工装已停用，不能装入设备。");

        var lockKeys = revision.Members
            .Select(static member => $"component:{member.ComponentId}")
            .Append($"revision:{revisionId:D}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var lockKey in lockKeys)
        {
            await using var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));", connection, transaction);
            lockCommand.Parameters.AddWithValue("key", lockKey);
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var member in revision.Members)
        {
            await using var componentCommand = new NpgsqlCommand(
                "SELECT payload::text FROM tooling_components WHERE component_id = @id;",
                connection,
                transaction);
            componentCommand.Parameters.AddWithValue("id", member.ComponentId);
            var payload = await componentCommand.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (payload is null or DBNull)
                throw new InvalidOperationException($"组件 {member.ComponentId} 不存在。");
            var component = Deserialize<ToolingComponent>((string)payload);
            if (!string.Equals(component.Status, "available", StringComparison.Ordinal))
                throw new InvalidOperationException($"组件 {member.ComponentId} 当前为 {component.Status}，不能装入设备。");
        }

        var componentIds = revision.Members.Select(static member => member.ComponentId).Distinct(StringComparer.Ordinal).ToArray();
        await using var occupiedCommand = new NpgsqlCommand(
            """
            SELECT installation.equipment_id
            FROM tooling_installations installation
            JOIN tooling_assembly_revisions active_revision
              ON active_revision.assembly_revision_id = installation.assembly_revision_id
            WHERE installation.removed_at IS NULL
              AND (
                installation.assembly_revision_id = @revision_id
                OR EXISTS (
                  SELECT 1
                  FROM jsonb_array_elements(COALESCE(active_revision.payload->'members', '[]'::jsonb)) member
                  WHERE member->>'componentId' = ANY(@component_ids)
                )
              )
            LIMIT 1;
            """,
            connection,
            transaction);
        occupiedCommand.Parameters.AddWithValue("revision_id", revisionId);
        occupiedCommand.Parameters.AddWithValue("component_ids", componentIds);
        var occupiedMachine = await occupiedCommand.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (!string.IsNullOrWhiteSpace(occupiedMachine))
        {
            throw new InvalidOperationException(
                $"该工装或其中的物理组件已装在设备 {occupiedMachine} 上，请先完成卸下。");
        }
    }

    private static async Task EnsureNoInstallationOverlapAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ToolingInstallation value,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT 1 FROM tooling_installations
            WHERE site_id = @site AND equipment_id = @equipment
              AND installed_at < COALESCE(@removed_at, 'infinity'::timestamptz)
              AND COALESCE(removed_at, 'infinity'::timestamptz) > @installed_at
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("site", value.SiteId);
        command.Parameters.AddWithValue("equipment", value.EquipmentId);
        command.Parameters.AddWithValue("installed_at", value.InstalledAt.UtcDateTime);
        command.Parameters.AddWithValue("removed_at", (object?)value.RemovedAt?.UtcDateTime ?? DBNull.Value);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("该设备在指定时间已经存在工装装卸记录，请先卸下当前工装或调整时间区间。");
    }

    private static async Task EnsureInstallationMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProductionContext value,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT 1 FROM tooling_installations
            WHERE installation_id = @id AND site_id = @site AND equipment_id = @equipment
              AND installed_at <= @at AND (removed_at IS NULL OR removed_at > @at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.ToolingInstallationId);
        command.Parameters.AddWithValue("site", value.SiteId);
        command.Parameters.AddWithValue("equipment", value.EquipmentId);
        command.Parameters.AddWithValue("at", value.ValidFrom.UtcDateTime);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("生产上下文引用的工装装卸记录在该设备和时间点无效。");
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        Action<NpgsqlCommand>? bind,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        bind?.Invoke(command);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private async Task<T?> GetAsync<T>(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        bind(command);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? default : Deserialize<T>((string)value);
    }

    private async Task<bool> ExistsAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        bind(command);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private async Task<bool> DeleteAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        bind(command);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static void AddJson<T>(NpgsqlCommand command, string name, T value)
        => command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));

    private static T Deserialize<T>(string value)
        => JsonSerializer.Deserialize<T>(value, JsonOptions)
           ?? throw new InvalidDataException($"无法反序列化 {typeof(T).Name}。");
}
