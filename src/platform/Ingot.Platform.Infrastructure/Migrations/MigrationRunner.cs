using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Migrations;

public sealed class MigrationRunner(
    IConfiguration configuration,
    ILogger<MigrationRunner> logger)
{

    private const long AdvisoryLockKey = 0x496E676F74;

    private const string ResourcePrefix = "Ingot.Platform.Infrastructure.Migrations.sql.";

    public async Task RunAsync(CancellationToken ct = default)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");

        var scripts = LoadScripts();
        if (scripts.Count == 0)
        {
            logger.LogWarning("未发现任何迁移脚本，跳过。");
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({AdvisoryLockKey});", ct).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS schema_version (
                  version    TEXT PRIMARY KEY,
                  name       TEXT NOT NULL,
                  checksum   TEXT NOT NULL,
                  applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """,
                ct).ConfigureAwait(false);

            var applied = await LoadAppliedAsync(connection, ct).ConfigureAwait(false);

            var pending = 0;
            foreach (var script in scripts)
            {
                if (applied.TryGetValue(script.Version, out var existingChecksum))
                {
                    if (!string.Equals(existingChecksum, script.Checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"迁移 {script.Version}({script.Name}) 的内容与已应用记录不一致（checksum 漂移）。" +
                            "已应用的迁移不可修改；请以新的编号迁移表达变更。");
                    }
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                await using (var command = new NpgsqlCommand(script.Sql, connection, transaction))
                {
                    command.CommandTimeout = 600;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await using (var record = new NpgsqlCommand(
                    "INSERT INTO schema_version(version, name, checksum) VALUES (@version, @name, @checksum);",
                    connection,
                    transaction))
                {
                    record.Parameters.AddWithValue("version", script.Version);
                    record.Parameters.AddWithValue("name", script.Name);
                    record.Parameters.AddWithValue("checksum", script.Checksum);
                    await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                pending++;
                logger.LogInformation("迁移已应用：{Version} {Name}", script.Version, script.Name);
            }

            logger.LogInformation(
                "数据库迁移完成：共 {Total} 个脚本，本次应用 {Applied} 个。",
                scripts.Count,
                pending);
        }
        finally
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({AdvisoryLockKey});", ct).ConfigureAwait(false);
        }
    }

    internal static string ComputeChecksum(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static (string Version, string Name) ParseResourceName(string resourceName)
    {

        var file = resourceName[ResourcePrefix.Length..];
        var stem = file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        var separator = stem.IndexOf('_', StringComparison.Ordinal);
        return separator <= 0
            ? (stem, stem)
            : (stem[..separator], stem[(separator + 1)..]);
    }

    private IReadOnlyList<MigrationScript> LoadScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var scripts = new List<MigrationScript>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                                           name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"无法读取迁移资源：{resourceName}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd();
            var (version, name) = ParseResourceName(resourceName);
            scripts.Add(new MigrationScript(version, name, sql, ComputeChecksum(sql)));
        }
        return scripts;
    }

    private static async Task<Dictionary<string, string>> LoadAppliedAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT version, checksum FROM schema_version;", connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            applied[reader.GetString(0)] = reader.GetString(1);
        return applied;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record MigrationScript(string Version, string Name, string Sql, string Checksum);
}
