using System.Text.Json;
using Ingot.Contracts.Acquisition;
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
