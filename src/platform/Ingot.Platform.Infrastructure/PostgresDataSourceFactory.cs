// 实现 PostgresDataSourceFactory 的 PostgreSQL 持久化适配，避免数据库细节泄漏到应用层。

using Npgsql;

namespace Ingot.Platform.Infrastructure;

internal static class PostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MinPoolSize = configuration.GetValue("Postgres:MinPoolSize", 2),
            MaxPoolSize = configuration.GetValue("Postgres:MaxPoolSize", 100),
            Timeout = configuration.GetValue("Postgres:ConnectionTimeoutSeconds", 15),
            CommandTimeout = configuration.GetValue("Postgres:CommandTimeoutSeconds", 30),
            KeepAlive = configuration.GetValue("Postgres:KeepAliveSeconds", 30)
        };
        if (settings.MinPoolSize > settings.MaxPoolSize)
            throw new InvalidOperationException("Postgres:MinPoolSize 不能大于 Postgres:MaxPoolSize。");
        return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
    }
}
