using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class PostgresAcquisitionProfileStore : IAcquisitionProfileStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresAcquisitionProfileStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS acquisition_profiles (
                  profile_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  edge_id TEXT NOT NULL,
                  status TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (profile_id, version),
                  CHECK (version > 0)
                );
                CREATE INDEX IF NOT EXISTS idx_acquisition_profiles_edge_status
                  ON acquisition_profiles(edge_id, status);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public Task<IReadOnlyList<AcquisitionProfile>> ListAsync(CancellationToken ct = default)
        => QueryAsync("ORDER BY profile_id, version DESC", null, ct);

    public Task<IReadOnlyList<AcquisitionProfile>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
        => QueryAsync("WHERE edge_id = @edge_id AND status = 'published' ORDER BY profile_id", edgeId, ct);

    public async Task<AcquisitionProfile?> GetAsync(string profileId, int version, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM acquisition_profiles WHERE profile_id = @profile_id AND version = @version;");
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("version", version);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : JsonSerializer.Deserialize<AcquisitionProfile>((string)payload, JsonOptions);
    }

    public async Task<AcquisitionProfile> UpsertAsync(AcquisitionProfile value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO acquisition_profiles(profile_id, version, edge_id, status, payload, updated_at)
            VALUES (@profile_id, @version, @edge_id, @status, @payload, @updated_at)
            ON CONFLICT (profile_id, version) DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("profile_id", value.ProfileId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("edge_id", value.EdgeId);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<bool> DeleteAsync(string profileId, int version, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "DELETE FROM acquisition_profiles WHERE profile_id = @profile_id AND version = @version;");
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("version", version);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<AcquisitionProfile> PublishExclusiveAsync(
        AcquisitionProfile published,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Serialize publication per logical profile. Row locks alone are insufficient when two
        // versions are published concurrently before either transaction has inserted a row.
        await using (var publicationLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended(@profile_id, 0));",
                         connection,
                         transaction))
        {
            publicationLock.Parameters.AddWithValue("profile_id", published.ProfileId);
            await publicationLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // 锁定并读取同 profile 的其他 published 版本（payload 为真相来源，必须同步改写其中的状态）。
        var retire = new List<(int Version, AcquisitionProfile Profile)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT version, payload::text FROM acquisition_profiles
            WHERE profile_id = @profile_id AND version <> @version AND status = 'published'
            FOR UPDATE;
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("profile_id", published.ProfileId);
            select.Parameters.AddWithValue("version", published.Version);
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var profile = JsonSerializer.Deserialize<AcquisitionProfile>(reader.GetString(1), JsonOptions)!;
                retire.Add((reader.GetInt32(0), profile));
            }
        }

        foreach (var (version, profile) in retire)
        {
            var retired = profile with { Status = ConfigurationStatuses.Retired, UpdatedAt = published.UpdatedAt };
            await using var update = new NpgsqlCommand(
                """
                UPDATE acquisition_profiles
                SET status = 'retired', payload = @payload, updated_at = @updated_at
                WHERE profile_id = @profile_id AND version = @version;
                """,
                connection,
                transaction);
            update.Parameters.AddWithValue("profile_id", published.ProfileId);
            update.Parameters.AddWithValue("version", version);
            update.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(retired, JsonOptions));
            update.Parameters.AddWithValue("updated_at", published.UpdatedAt.UtcDateTime);
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO acquisition_profiles(profile_id, version, edge_id, status, payload, updated_at)
            VALUES (@profile_id, @version, @edge_id, @status, @payload, @updated_at)
            ON CONFLICT (profile_id, version) DO UPDATE SET
              edge_id = EXCLUDED.edge_id,
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue("profile_id", published.ProfileId);
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

    private async Task<IReadOnlyList<AcquisitionProfile>> QueryAsync(
        string clause,
        string? edgeId,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand($"SELECT payload::text FROM acquisition_profiles {clause};");
        if (edgeId is not null) command.Parameters.AddWithValue("edge_id", edgeId);
        var values = new List<AcquisitionProfile>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<AcquisitionProfile>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
