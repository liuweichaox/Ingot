using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingot.Platform.Infrastructure.HealthChecks;

/// <summary>
/// 检查 Platform 侧 SQLite 存储是否可用。
/// </summary>
public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public SqliteHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = _configuration["Platform:DatabasePath"]
                ?? _configuration["Central:DatabasePath"]
                ?? "Data/platform.db";
            if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            _ = cmd.ExecuteScalar();

            return Task.FromResult(HealthCheckResult.Healthy("sqlite ok"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("sqlite unavailable", ex));
        }
    }
}

