using System.Collections.Concurrent;
using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ingot.Edge.Infrastructure.State;

public sealed class ContextStore : IEdgeContextStore
{
    private readonly ConcurrentDictionary<string, string> _context = new();
    private readonly string _connectionString;
    private readonly object _databaseLock = new();
    private readonly ILogger<ContextStore> _logger;
    private readonly IMetricsCollector? _metrics;

    public ContextStore(
        IConfiguration configuration,
        ILogger<ContextStore> logger,
        IMetricsCollector? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;

        var databasePath = configuration["Context:DatabasePath"] ?? "Data/context.db";
        if (!Path.IsPathRooted(databasePath))
            databasePath = Path.Combine(AppContext.BaseDirectory, databasePath);

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        EnsureSchema();
        Load();
        RecordCount();
    }

    public string? Get(ObjectRef asset, string key)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return string.IsNullOrWhiteSpace(key)
            ? null
            : _context.GetValueOrDefault(GetKey(asset, key));
    }

    public async Task SetAsync(ObjectRef asset, string key, string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("生产信息项不能为空。", nameof(key));

        var normalizedKey = key.Trim();
        var normalizedValue = value ?? string.Empty;
        var memoryKey = GetKey(asset, normalizedKey);

        await Task.Run(() =>
        {
            lock (_databaseLock)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                                      INSERT INTO context_state(
                                        asset_type, asset_id, context_key, context_value, updated_utc)
                                      VALUES (
                                        $asset_type, $asset_id, $context_key, $context_value, $updated_utc)
                                      ON CONFLICT(asset_type, asset_id, context_key) DO UPDATE SET
                                        context_value = excluded.context_value,
                                        updated_utc = excluded.updated_utc;
                                      """;
                command.Parameters.AddWithValue("$asset_type", asset.Type);
                command.Parameters.AddWithValue("$asset_id", asset.Id);
                command.Parameters.AddWithValue("$context_key", normalizedKey);
                command.Parameters.AddWithValue("$context_value", normalizedValue);
                command.Parameters.AddWithValue("$updated_utc", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
                _context[memoryKey] = normalizedValue;
            }
        }, ct).ConfigureAwait(false);

        RecordCount();
    }

    public IReadOnlyDictionary<string, string> Snapshot(ObjectRef asset, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(keys);

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var value = Get(asset, key);
            if (value is not null)
                snapshot[key] = value;
        }

        return snapshot;
    }

    private void EnsureSchema()
    {
        lock (_databaseLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS context_state (
                                    asset_type    TEXT NOT NULL,
                                    asset_id      TEXT NOT NULL,
                                    context_key   TEXT NOT NULL,
                                    context_value TEXT NOT NULL,
                                    updated_utc   TEXT NOT NULL,
                                    PRIMARY KEY(asset_type, asset_id, context_key)
                                  );
                                  """;
            command.ExecuteNonQuery();
        }
    }

    private void Load()
    {
        lock (_databaseLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT asset_type, asset_id, context_key, context_value FROM context_state;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var asset = new ObjectRef(reader.GetString(0), reader.GetString(1));
                _context[GetKey(asset, reader.GetString(2))] = reader.GetString(3);
            }
        }
    }

    private void RecordCount()
    {
        try
        {
            _metrics?.RecordContextStateEntries(_context.Count);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "记录关联信息状态指标失败");
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string GetKey(ObjectRef asset, string key) =>
        $"{asset.Type}\u001f{asset.Id}\u001f{key}";
}
