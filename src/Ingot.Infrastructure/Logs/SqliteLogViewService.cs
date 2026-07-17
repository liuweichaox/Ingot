using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Logs;

/// <summary>
///     基于 SQLite 的日志查看服务实现
/// </summary>
public class SqliteLogViewService : ILogViewService, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly string _dbPath;

    public SqliteLogViewService(IOptions<LogOptions>? options = null)
    {
        // 从配置或默认路径获取数据库路径
        var configuredPath = options?.Value?.DatabasePath ?? "Data/logs.db";

        // 如果是相对路径，转换为绝对路径
        _dbPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        // 确保目录存在
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        InitializeDatabase();
    }

    /// <summary>
    ///     初始化数据库表结构
    ///     创建索引和 FTS5 全文搜索表
    /// </summary>
    private void InitializeDatabase()
    {
        var sql = @"
            -- 创建复合索引以提高查询性能
            CREATE INDEX IF NOT EXISTS idx_logs_level_timestamp ON Logs(Level, TimeStamp DESC);

            -- 创建 FTS5 全文搜索表（用于关键词搜索）
            CREATE VIRTUAL TABLE IF NOT EXISTS LogsFts USING fts5(
                Message,
                Exception,
                Properties,
                content='Logs',
                content_rowid='Id'
            );

            -- 创建触发器，自动同步 FTS5 表
            CREATE TRIGGER IF NOT EXISTS logs_fts_insert AFTER INSERT ON Logs BEGIN
                INSERT INTO LogsFts(rowid, Message, Exception, Properties)
                VALUES (new.Id, new.Message, COALESCE(new.Exception, ''), COALESCE(new.Properties, ''));
            END;

            CREATE TRIGGER IF NOT EXISTS logs_fts_delete AFTER DELETE ON Logs BEGIN
                DELETE FROM LogsFts WHERE rowid = old.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS logs_fts_update AFTER UPDATE ON Logs BEGIN
                DELETE FROM LogsFts WHERE rowid = old.Id;
                INSERT INTO LogsFts(rowid, Message, Exception, Properties)
                VALUES (new.Id, new.Message, COALESCE(new.Exception, ''), COALESCE(new.Properties, ''));
            END;
        ";

        using var command = new SqliteCommand(sql, _connection);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Logs 表可能尚未由 MicrosoftSqliteSink 创建，此处忽略
        }
    }

    /// <summary>
    ///     获取日志条目列表
    /// </summary>
    public async Task<(List<LogEntry> Entries, int TotalCount)> GetLogsAsync(
        string? level = null,
        string? keyword = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var whereConditions = new List<string>();
                var parameters = new List<SqliteParameter>();

                // 按级别过滤
                if (!string.IsNullOrWhiteSpace(level))
                {
                    whereConditions.Add("l.Level = @level");
                    parameters.Add(new SqliteParameter("@level", level));
                }

                // 按关键词过滤（使用 FTS5 全文搜索）
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    // FTS5 搜索语法：搜索所有字段
                    whereConditions.Add("l.Id IN (SELECT rowid FROM LogsFts WHERE LogsFts MATCH @keyword)");
                    // 转义特殊字符
                    var escapedKeyword = keyword.Replace("\"", "\"\"");
                    parameters.Add(new SqliteParameter("@keyword", escapedKeyword));
                }

                var whereClause = whereConditions.Count > 0
                    ? "WHERE " + string.Join(" AND ", whereConditions)
                    : "";

                // 获取总数
                var countSql = $@"
                SELECT COUNT(*) 
                FROM Logs l
                {whereClause}
            ";

                int totalCount;
                using (var countCommand = new SqliteCommand(countSql, _connection))
                {
                    foreach (var param in parameters)
                    {
                        countCommand.Parameters.Add(param);
                    }
                    totalCount = Convert.ToInt32(countCommand.ExecuteScalar());
                }

                // 查询数据（适配 Serilog.Sinks.SQLite 的表结构）
                var querySql = $@"
                SELECT 
                    l.TimeStamp,
                    l.Level,
                    l.Properties,
                    l.Message,
                    l.Exception
                FROM Logs l
                {whereClause}
                ORDER BY l.TimeStamp DESC
                LIMIT @take OFFSET @skip
            ";

                var entries = new List<LogEntry>();
                using (var queryCommand = new SqliteCommand(querySql, _connection))
                {
                    foreach (var param in parameters)
                    {
                        queryCommand.Parameters.Add(param);
                    }
                    queryCommand.Parameters.Add(new SqliteParameter("@take", take));
                    queryCommand.Parameters.Add(new SqliteParameter("@skip", skip));

                    using var reader = queryCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        var rowTimestamp = DateTime.Parse(reader.GetString(0));
                        var rowLevel = reader.GetString(1);
                        var properties = reader.IsDBNull(2) ? null : reader.GetString(2);
                        var message = reader.GetString(3);
                        var exception = reader.IsDBNull(4) ? null : reader.GetString(4);

                        var source = ExtractSourceFromProperties(properties);

                        entries.Add(new LogEntry
                        {
                            Timestamp = rowTimestamp,
                            Level = rowLevel,
                            Source = source,
                            Message = message,
                            Exception = exception
                        });
                    }
                }

                return (entries, totalCount);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    ///     获取可用的日志级别列表
    /// </summary>
    public List<string> GetAvailableLevels()
    {
        _connectionLock.Wait();
        try
        {
            var sql = @"
            SELECT DISTINCT Level 
            FROM Logs 
            ORDER BY 
                CASE Level
                    WHEN 'Verbose' THEN 1
                    WHEN 'Debug' THEN 2
                    WHEN 'Information' THEN 3
                    WHEN 'Warning' THEN 4
                    WHEN 'Error' THEN 5
                    WHEN 'Fatal' THEN 6
                    ELSE 7
                END
        ";

            var levels = new List<string>();
            using var command = new SqliteCommand(sql, _connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                levels.Add(reader.GetString(0));
            }

            // 如果没有找到任何级别，返回默认列表
            if (levels.Count == 0)
            {
                return new List<string> { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };
            }

            return levels;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    ///     从 Properties JSON 中提取 SourceContext
    /// </summary>
    private string ExtractSourceFromProperties(string? properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(properties);
            if (doc.RootElement.TryGetProperty("SourceContext", out var sourceContext))
            {
                return sourceContext.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // 忽略 JSON 解析错误
        }

        return string.Empty;
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _connectionLock?.Dispose();
    }
}

/// <summary>
///     日志配置选项
/// </summary>
public class LogOptions
{
    /// <summary>
    ///     SQLite 数据库路径
    ///     支持相对路径（相对于应用程序目录）和绝对路径
    ///     默认值：Data/logs.db
    /// </summary>
    public string DatabasePath { get; set; } = "Data/logs.db";

    /// <summary>
    ///     SQLite log retention in days. Values less than or equal to 0 disable cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
