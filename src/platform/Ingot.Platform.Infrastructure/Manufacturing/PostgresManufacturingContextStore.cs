using System.Text.Json;
using Ingot.Contracts.Manufacturing;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Manufacturing;

public sealed class PostgresManufacturingContextStore : IManufacturingContextStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresManufacturingContextStore(IConfiguration configuration)
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
                CREATE TABLE IF NOT EXISTS tooling_types (
                  tooling_type_code TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (tooling_type_code, version),
                  CHECK (version > 0)
                );

                CREATE TABLE IF NOT EXISTS tooling_component_types (
                  component_type_code TEXT PRIMARY KEY,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS tooling_components (
                  component_id TEXT PRIMARY KEY,
                  component_type_code TEXT NOT NULL,
                  tooling_type_code TEXT,
                  role_code TEXT,
                  serial_no TEXT NOT NULL UNIQUE,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );
                ALTER TABLE tooling_components ADD COLUMN IF NOT EXISTS component_type_code TEXT;
                ALTER TABLE tooling_components ALTER COLUMN tooling_type_code DROP NOT NULL;
                ALTER TABLE tooling_components ALTER COLUMN role_code DROP NOT NULL;
                UPDATE tooling_components
                  SET component_type_code = COALESCE(NULLIF(component_type_code, ''), NULLIF(role_code, ''), 'uncategorized')
                  WHERE component_type_code IS NULL OR component_type_code = '';
                UPDATE tooling_components
                  SET payload = (payload - 'toolingTypeCode' - 'roleCode') || jsonb_build_object('componentTypeCode', component_type_code)
                  WHERE NOT payload ? 'componentTypeCode';
                ALTER TABLE tooling_components ALTER COLUMN component_type_code SET NOT NULL;
                CREATE INDEX IF NOT EXISTS idx_tooling_components_component_type
                  ON tooling_components(component_type_code);

                CREATE TABLE IF NOT EXISTS tooling_assemblies (
                  mold_id TEXT PRIMARY KEY,
                  tooling_type_code TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS tooling_assembly_revisions (
                  assembly_revision_id UUID PRIMARY KEY,
                  mold_id TEXT NOT NULL REFERENCES tooling_assemblies(mold_id),
                  revision INTEGER NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  UNIQUE (mold_id, revision),
                  CHECK (revision > 0)
                );

                CREATE TABLE IF NOT EXISTS tooling_installations (
                  installation_id UUID PRIMARY KEY,
                  machine_id TEXT NOT NULL,
                  assembly_revision_id UUID NOT NULL REFERENCES tooling_assembly_revisions(assembly_revision_id),
                  installed_at TIMESTAMPTZ NOT NULL,
                  removed_at TIMESTAMPTZ,
                  source TEXT NOT NULL,
                  command_id TEXT UNIQUE,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  CHECK (removed_at IS NULL OR removed_at > installed_at)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_tooling_installations_active_machine
                  ON tooling_installations(machine_id) WHERE removed_at IS NULL;
                CREATE INDEX IF NOT EXISTS idx_tooling_installations_machine_time
                  ON tooling_installations(machine_id, installed_at, removed_at);

                CREATE TABLE IF NOT EXISTS production_contexts (
                  context_id UUID PRIMARY KEY,
                  machine_id TEXT NOT NULL,
                  tooling_installation_id UUID NOT NULL REFERENCES tooling_installations(installation_id),
                  valid_from TIMESTAMPTZ NOT NULL,
                  valid_to TIMESTAMPTZ,
                  source TEXT NOT NULL,
                  command_id TEXT UNIQUE,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  CHECK (valid_to IS NULL OR valid_to > valid_from)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_production_contexts_active_machine
                  ON production_contexts(machine_id) WHERE valid_to IS NULL;
                CREATE INDEX IF NOT EXISTS idx_production_contexts_machine_time
                  ON production_contexts(machine_id, valid_from, valid_to);
                ALTER TABLE production_contexts ADD COLUMN IF NOT EXISTS command_id TEXT;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_production_contexts_command_id
                  ON production_contexts(command_id) WHERE command_id IS NOT NULL;
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

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
        var existing = await GetAssemblyAsync(value.MoldId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ToolingTypeCode != value.ToolingTypeCode)
            throw new InvalidOperationException("已创建模具的工装类型不可修改；请新建模具编号。");
        _ = await GetLatestToolingTypeAsync(value.ToolingTypeCode, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("工装类型不存在。");
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_assemblies(mold_id, tooling_type_code, payload, updated_at)
            VALUES (@id, @type, @payload, @updated_at)
            ON CONFLICT (mold_id) DO UPDATE SET
              tooling_type_code = EXCLUDED.tooling_type_code,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.MoldId);
        command.Parameters.AddWithValue("type", value.ToolingTypeCode);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ToolingAssembly>> ListAssembliesAsync(CancellationToken ct = default)
        => ListAsync<ToolingAssembly>(
            "SELECT payload::text FROM tooling_assemblies ORDER BY mold_id;", null, ct);

    public async Task<bool> DeleteAssemblyAsync(string moldId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM tooling_assembly_revisions WHERE mold_id = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", moldId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该工装已存在组合版本，不能删除；请将工装停用。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_assemblies WHERE mold_id = @id;",
            command => command.Parameters.AddWithValue("id", moldId), ct).ConfigureAwait(false);
    }

    public async Task<ToolingAssemblyRevision> CreateAssemblyRevisionAsync(
        ToolingAssemblyRevision value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var assembly = await GetAssemblyAsync(value.MoldId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("模具编号不存在。");
        var type = await GetLatestToolingTypeAsync(assembly.ToolingTypeCode, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("模具对应的工装类型不存在。");
        await ValidateAssemblyMembersAsync(value, type, ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tooling_assembly_revisions(
              assembly_revision_id, mold_id, revision, payload, created_at)
            VALUES (@id, @mold_id, @revision, @payload, @created_at)
            ON CONFLICT DO NOTHING;
            """);
        command.Parameters.AddWithValue("id", value.AssemblyRevisionId);
        command.Parameters.AddWithValue("mold_id", value.MoldId);
        command.Parameters.AddWithValue("revision", value.Revision);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new InvalidOperationException("该模具组合版本已存在；组合版本不可修改，请创建下一版本。");
        return value;
    }

    public Task<IReadOnlyList<ToolingAssemblyRevision>> ListAssemblyRevisionsAsync(
        string? moldId = null,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM tooling_assembly_revisions " +
                           "WHERE (@mold_id = '' OR mold_id = @mold_id) ORDER BY mold_id, revision DESC;";
        return ListAsync<ToolingAssemblyRevision>(sql,
            command => command.Parameters.AddWithValue("mold_id", moldId?.Trim() ?? ""), ct);
    }

    public async Task<bool> DeleteAssemblyRevisionAsync(Guid assemblyRevisionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM tooling_installations WHERE assembly_revision_id = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", assemblyRevisionId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该组合版本已有装模记录，不能删除。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_assembly_revisions WHERE assembly_revision_id = @id;",
            command => command.Parameters.AddWithValue("id", assemblyRevisionId), ct).ConfigureAwait(false);
    }

    public async Task<ToolingInstallation> CreateInstallationAsync(
        ToolingInstallation value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(value.CommandId))
        {
            var existing = await GetByCommandIdAsync(connection, transaction, value.CommandId, ct).ConfigureAwait(false);
            if (existing is not null)
                return existing;
        }

        await EnsureRevisionExistsAsync(connection, transaction, value.AssemblyRevisionId, ct).ConfigureAwait(false);
        await EnsureNoInstallationOverlapAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tooling_installations(
              installation_id, machine_id, assembly_revision_id, installed_at, removed_at,
              source, command_id, payload, created_at)
            VALUES (@id, @machine, @revision, @installed_at, @removed_at,
                    @source, @command_id, @payload, @created_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.InstallationId);
        command.Parameters.AddWithValue("machine", value.MachineId);
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
        Guid installationId,
        DateTimeOffset removedAt,
        string? actor,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var existing = await GetInstallationAsync(connection, transaction, installationId, ct).ConfigureAwait(false);
        if (existing is null)
            return null;
        if (existing.RemovedAt.HasValue)
            return existing;
        if (removedAt <= existing.InstalledAt)
            throw new InvalidOperationException("卸模时间必须晚于装模时间。");

        var at = removedAt.ToUniversalTime();
        var activeContext = await GetActiveProductionContextAsync(connection, transaction, installationId, ct)
            .ConfigureAwait(false);
        if (activeContext is not null)
        {
            var closedContext = activeContext with
            {
                ValidTo = at,
                Actor = actor?.Trim() ?? activeContext.Actor,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await using var closeContext = new NpgsqlCommand(
                """
                UPDATE production_contexts
                SET valid_to = @at, payload = @payload, updated_at = @updated_at
                WHERE context_id = @id AND valid_to IS NULL;
                """, connection, transaction);
            closeContext.Parameters.AddWithValue("id", closedContext.ContextId);
            closeContext.Parameters.AddWithValue("at", at.UtcDateTime);
            AddJson(closeContext, "payload", closedContext);
            closeContext.Parameters.AddWithValue("updated_at", closedContext.UpdatedAt.UtcDateTime);
            await closeContext.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var updated = existing with { RemovedAt = at, Actor = actor?.Trim() ?? existing.Actor };
        await using var command = new NpgsqlCommand(
            """
            UPDATE tooling_installations
            SET removed_at = @removed_at, payload = @payload
            WHERE installation_id = @id AND removed_at IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", installationId);
        command.Parameters.AddWithValue("removed_at", updated.RemovedAt.Value.UtcDateTime);
        AddJson(command, "payload", updated);
        var changed = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return changed ? updated : existing;
    }

    public Task<IReadOnlyList<ToolingInstallation>> ListInstallationsAsync(
        string? machineId = null,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM tooling_installations " +
                           "WHERE (@machine = '' OR machine_id = @machine) " +
                           "AND (NOT @active OR removed_at IS NULL) ORDER BY installed_at DESC;";
        return ListAsync<ToolingInstallation>(sql, command =>
        {
            command.Parameters.AddWithValue("machine", machineId?.Trim() ?? "");
            command.Parameters.AddWithValue("active", activeOnly);
        }, ct);
    }

    public async Task<bool> DeleteInstallationAsync(Guid installationId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM production_contexts WHERE tooling_installation_id = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", installationId), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该装模记录已被生产配置引用，不能删除；错误记录应先结束并保留追溯。");
        }
        return await DeleteAsync(
            "DELETE FROM tooling_installations WHERE installation_id = @id;",
            command => command.Parameters.AddWithValue("id", installationId), ct).ConfigureAwait(false);
    }

    public async Task<ProductionContext> StartProductionContextAsync(
        ProductionContext value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(value.CommandId))
        {
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

        await using (var invalidActive = new NpgsqlCommand(
            """
            SELECT 1 FROM production_contexts
            WHERE machine_id = @machine AND valid_to IS NULL AND valid_from >= @valid_from
            LIMIT 1;
            """, connection, transaction))
        {
            invalidActive.Parameters.AddWithValue("machine", value.MachineId);
            invalidActive.Parameters.AddWithValue("valid_from", value.ValidFrom.UtcDateTime);
            if (await invalidActive.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("新的生产上下文生效时间必须晚于当前上下文开始时间。");
        }

        await using (var ownership = new NpgsqlCommand(
            """
            SELECT source FROM production_contexts
            WHERE machine_id = @machine AND valid_to IS NULL
            LIMIT 1 FOR UPDATE;
            """, connection, transaction))
        {
            ownership.Parameters.AddWithValue("machine", value.MachineId);
            var activeSource = await ownership.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (string.Equals(activeSource, "mes", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value.Source, "manual", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该设备当前生产信息由 MES 管理，不能在平台重复人工录入。");
            }
        }

        await using (var close = new NpgsqlCommand(
            """
            UPDATE production_contexts
            SET valid_to = @valid_from,
                payload = jsonb_set(payload, '{validTo}', to_jsonb(@valid_from::timestamptz), true),
                updated_at = now()
            WHERE machine_id = @machine AND valid_to IS NULL AND valid_from < @valid_from;
            """, connection, transaction))
        {
            close.Parameters.AddWithValue("machine", value.MachineId);
            close.Parameters.AddWithValue("valid_from", value.ValidFrom.UtcDateTime);
            await close.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO production_contexts(
              context_id, machine_id, tooling_installation_id, valid_from, valid_to, source, command_id, payload, updated_at)
            VALUES (@id, @machine, @installation, @valid_from, @valid_to, @source, @command_id, @payload, @updated_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.ContextId);
        command.Parameters.AddWithValue("machine", value.MachineId);
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
        Guid contextId,
        DateTimeOffset validTo,
        string? actor,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var existing = await GetProductionContextAsync(contextId, ct).ConfigureAwait(false);
        if (existing is null)
            return null;
        if (existing.ValidTo.HasValue)
            return existing;
        if (validTo <= existing.ValidFrom)
            throw new InvalidOperationException("结束时间必须晚于生产上下文开始时间。");
        var updated = existing with
        {
            ValidTo = validTo.ToUniversalTime(),
            Actor = actor?.Trim() ?? existing.Actor,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE production_contexts
            SET valid_to = @valid_to, payload = @payload, updated_at = @updated_at
            WHERE context_id = @id AND valid_to IS NULL;
            """);
        command.Parameters.AddWithValue("id", contextId);
        command.Parameters.AddWithValue("valid_to", updated.ValidTo.Value.UtcDateTime);
        AddJson(command, "payload", updated);
        command.Parameters.AddWithValue("updated_at", updated.UpdatedAt.UtcDateTime);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0 ? updated : existing;
    }

    public Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(
        string? machineId = null,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        const string sql = "SELECT payload::text FROM production_contexts " +
                           "WHERE (@machine = '' OR machine_id = @machine) " +
                           "AND (NOT @active OR valid_to IS NULL) ORDER BY valid_from DESC;";
        return ListAsync<ProductionContext>(sql, command =>
        {
            command.Parameters.AddWithValue("machine", machineId?.Trim() ?? "");
            command.Parameters.AddWithValue("active", activeOnly);
        }, ct);
    }

    public async Task<bool> DeleteProductionContextAsync(Guid contextId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (await ExistsAsync(
                "SELECT 1 FROM production_events WHERE context->>'production_context_id' = @id LIMIT 1;",
                command => command.Parameters.AddWithValue("id", contextId.ToString("D")), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("该生产配置已被生产事件固化引用，不能删除；请结束其生效区间。");
        }
        return await DeleteAsync(
            "DELETE FROM production_contexts WHERE context_id = @id;",
            command => command.Parameters.AddWithValue("id", contextId), ct).ConfigureAwait(false);
    }

    public async Task<ResolvedProductionContext?> ResolveAsync(
        string machineId,
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
            JOIN tooling_assemblies a ON a.mold_id = ar.mold_id
            WHERE pc.machine_id = @machine
              AND pc.valid_from <= @at AND (pc.valid_to IS NULL OR pc.valid_to > @at)
              AND ti.installed_at <= @at AND (ti.removed_at IS NULL OR ti.removed_at > @at)
            ORDER BY pc.valid_from DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("machine", machineId.Trim());
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

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
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
            "SELECT payload::text FROM tooling_assemblies WHERE mold_id = @id;",
            command => command.Parameters.AddWithValue("id", id), ct).ConfigureAwait(false);

    private static async Task<ToolingInstallation?> GetInstallationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM tooling_installations WHERE installation_id = @id FOR UPDATE;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
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

    private async Task<ProductionContext?> GetProductionContextAsync(Guid id, CancellationToken ct)
        => await GetAsync<ProductionContext>(
            "SELECT payload::text FROM production_contexts WHERE context_id = @id;",
            command => command.Parameters.AddWithValue("id", id), ct).ConfigureAwait(false);

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
        => string.Equals(left.MachineId, right.MachineId, StringComparison.Ordinal) &&
           string.Equals(left.ProductSeries, right.ProductSeries, StringComparison.Ordinal) &&
           string.Equals(left.ProductCode, right.ProductCode, StringComparison.Ordinal) &&
           string.Equals(left.RecipeId, right.RecipeId, StringComparison.Ordinal) &&
           string.Equals(left.RecipeVersion, right.RecipeVersion, StringComparison.Ordinal) &&
           left.ToolingInstallationId == right.ToolingInstallationId &&
           left.ValidFrom == right.ValidFrom &&
           string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
           string.Equals(left.ExternalOrderRef, right.ExternalOrderRef, StringComparison.Ordinal) &&
           string.Equals(left.ExternalBatchRef, right.ExternalBatchRef, StringComparison.Ordinal) &&
           string.Equals(left.MaterialLotRef, right.MaterialLotRef, StringComparison.Ordinal);

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

    private static async Task EnsureRevisionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid revisionId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM tooling_assembly_revisions WHERE assembly_revision_id = @id;", connection, transaction);
        command.Parameters.AddWithValue("id", revisionId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("模具组合版本不存在。");
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
            WHERE machine_id = @machine
              AND installed_at < COALESCE(@removed_at, 'infinity'::timestamptz)
              AND COALESCE(removed_at, 'infinity'::timestamptz) > @installed_at
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("machine", value.MachineId);
        command.Parameters.AddWithValue("installed_at", value.InstalledAt.UtcDateTime);
        command.Parameters.AddWithValue("removed_at", (object?)value.RemovedAt?.UtcDateTime ?? DBNull.Value);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("该设备在指定时间已经存在装模记录，请先卸模或调整时间区间。");
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
            WHERE installation_id = @id AND machine_id = @machine
              AND installed_at <= @at AND (removed_at IS NULL OR removed_at > @at);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", value.ToolingInstallationId);
        command.Parameters.AddWithValue("machine", value.MachineId);
        command.Parameters.AddWithValue("at", value.ValidFrom.UtcDateTime);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new InvalidOperationException("生产上下文引用的装模记录在该设备和时间点无效。");
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
