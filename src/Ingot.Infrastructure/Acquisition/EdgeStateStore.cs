using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     采集周期状态管理器。热路径使用内存字典，活跃周期同时镜像到本地 SQLite，
///     以便进程重启后恢复条件采集的 active cycle。
/// </summary>
public class EdgeStateStore : IEdgeStateStore
{
    private readonly ConcurrentDictionary<string, AcquisitionCycle> _activeCycles = new();
    private readonly ConcurrentDictionary<string, string> _context = new();
    private readonly string _connectionString;
    private readonly object _dbLock = new();
    private readonly ILogger<EdgeStateStore> _logger;
    private readonly IMetricsCollector? _metrics;

    public EdgeStateStore(
        IConfiguration configuration,
        ILogger<EdgeStateStore> logger,
        IMetricsCollector? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;

        var dbPath = configuration["Acquisition:StateStore:DatabasePath"] ?? "Data/acquisition-state.db";
        if (!Path.IsPathRooted(dbPath))
            dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureSchema();
        LoadActiveCycles();
        LoadContext();
        RecordContextCountMetric();
    }

    public AcquisitionCycle StartCycle(string sourceCode, string channelCode, string measurement)
    {
        var key = $"{sourceCode}:{channelCode}:{measurement}";
        var cycle = new AcquisitionCycle
        {
            CycleId = Guid.NewGuid().ToString(),
            Measurement = measurement,
            SourceCode = sourceCode,
            ChannelCode = channelCode
        };
        SaveCycle(key, cycle);
        return cycle;
    }

    public AcquisitionCycle? EndCycle(string sourceCode, string channelCode, string measurement)
    {
        var key = $"{sourceCode}:{channelCode}:{measurement}";
        return DeleteCycle(key);
    }

    public AcquisitionCycle? GetActiveCycle(string sourceCode, string channelCode, string measurement)
    {
        _activeCycles.TryGetValue(GetKey(sourceCode, channelCode, measurement), out var cycle);
        return cycle;
    }

    public string? Get(ObjectRef asset, string key)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return _context.TryGetValue(GetContextKey(asset, key), out var value) ? value : null;
    }

    public async Task SetAsync(ObjectRef asset, string key, string value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("上下文键不能为空。", nameof(key));

        var normalizedKey = key.Trim();
        var normalizedValue = value ?? string.Empty;
        var memoryKey = GetContextKey(asset, normalizedKey);

        await Task.Run(() =>
        {
            lock (_dbLock)
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
        RecordContextCountMetric();
    }

    public IReadOnlyDictionary<string, string> Snapshot(ObjectRef asset, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(keys);

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Where(static key => !string.IsNullOrWhiteSpace(key)))
        {
            var value = Get(asset, key);
            if (value is not null)
                snapshot[key] = value;
        }

        return snapshot;
    }

    private void EnsureSchema()
    {
        lock (_dbLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              CREATE TABLE IF NOT EXISTS active_cycles (
                                cycle_key    TEXT PRIMARY KEY,
                                cycle_id     TEXT NOT NULL,
                                source_code  TEXT NOT NULL,
                                channel_code TEXT NOT NULL,
                                measurement  TEXT NOT NULL,
                                updated_utc  TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS context_state (
                                asset_type    TEXT NOT NULL,
                                asset_id      TEXT NOT NULL,
                                context_key   TEXT NOT NULL,
                                context_value TEXT NOT NULL,
                                updated_utc   TEXT NOT NULL,
                                PRIMARY KEY(asset_type, asset_id, context_key)
                              );
                              """;
            cmd.ExecuteNonQuery();
            MigrateActiveCycleSourceColumn(conn);
        }
    }

    private static void MigrateActiveCycleSourceColumn(SqliteConnection connection)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(active_cycles);";
        using var reader = inspect.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            columns.Add(reader.GetString(1));
        reader.Close();

        if (columns.Contains("source_code") || !columns.Contains("plc_code"))
            return;

        using var migrate = connection.CreateCommand();
        migrate.CommandText = "ALTER TABLE active_cycles RENAME COLUMN plc_code TO source_code;";
        migrate.ExecuteNonQuery();
    }

    private void LoadContext()
    {
        lock (_dbLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT asset_type, asset_id, context_key, context_value
                                  FROM context_state;
                                  """;

            using var reader = command.ExecuteReader();
            var count = 0;
            while (reader.Read())
            {
                var asset = new ObjectRef(reader.GetString(0), reader.GetString(1));
                _context[GetContextKey(asset, reader.GetString(2))] = reader.GetString(3);
                count++;
            }

            if (count > 0)
                _logger.LogInformation("已从边缘状态库恢复 {Count} 个业务上下文项", count);
        }
    }

    private void LoadActiveCycles()
    {
        lock (_dbLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              SELECT cycle_id, source_code, channel_code, measurement
                              FROM active_cycles;
                              """;

            using var reader = cmd.ExecuteReader();
            var count = 0;
            while (reader.Read())
            {
                var cycle = new AcquisitionCycle
                {
                    CycleId = reader.GetString(0),
                    SourceCode = reader.GetString(1),
                    ChannelCode = reader.GetString(2),
                    Measurement = reader.GetString(3)
                };

                _activeCycles[GetKey(cycle.SourceCode, cycle.ChannelCode, cycle.Measurement)] = cycle;
                count++;
            }

            if (count > 0)
                _logger.LogInformation("已从本地状态库恢复 {Count} 个活跃采集周期", count);
        }
    }

    private void SaveCycle(string key, AcquisitionCycle cycle)
    {
        lock (_dbLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO active_cycles(cycle_key, cycle_id, source_code, channel_code, measurement, updated_utc)
                              VALUES ($cycle_key, $cycle_id, $source_code, $channel_code, $measurement, $updated_utc)
                              ON CONFLICT(cycle_key) DO UPDATE SET
                                cycle_id = excluded.cycle_id,
                                source_code = excluded.source_code,
                                channel_code = excluded.channel_code,
                                measurement = excluded.measurement,
                                updated_utc = excluded.updated_utc;
                              """;
            cmd.Parameters.AddWithValue("$cycle_key", key);
            cmd.Parameters.AddWithValue("$cycle_id", cycle.CycleId);
            cmd.Parameters.AddWithValue("$source_code", cycle.SourceCode);
            cmd.Parameters.AddWithValue("$channel_code", cycle.ChannelCode);
            cmd.Parameters.AddWithValue("$measurement", cycle.Measurement);
            cmd.Parameters.AddWithValue("$updated_utc", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
            _activeCycles[key] = cycle;
        }
    }

    private AcquisitionCycle? DeleteCycle(string key)
    {
        lock (_dbLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM active_cycles WHERE cycle_key = $cycle_key;";
            cmd.Parameters.AddWithValue("$cycle_key", key);
            cmd.ExecuteNonQuery();
            return _activeCycles.TryRemove(key, out var cycle) ? cycle : null;
        }
    }

    private void RecordContextCountMetric()
    {
        try
        {
            _metrics?.RecordContextStateEntries(_context.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "上下文状态已经持久化，但记录 context_state_entries 指标失败。");
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static string GetKey(string sourceCode, string channelCode, string measurement) =>
        $"{sourceCode}:{channelCode}:{measurement}";

    private static string GetContextKey(ObjectRef asset, string key) =>
        $"{asset.Type}\u001f{asset.Id}\u001f{key}";
}
