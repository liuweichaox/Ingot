using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Edge.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Infrastructure.Logs;

public class SqliteLogViewService : ILogViewService, IDisposable
{
    private const string OperatorAudienceSql =
        """
        lower(l.Level) IN ('warning', 'error', 'fatal') OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'acquisition') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'protocol') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'device') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'configuration') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'deployment') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'outbox') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'shipment') > 0 OR
        instr(lower(CASE WHEN json_valid(l.Properties) THEN coalesce(json_extract(l.Properties, '$.SourceContext'), '') ELSE '' END), 'delivery') > 0
        """;

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly string _dbPath;

    public SqliteLogViewService(IOptions<LogOptions>? options = null)
    {

        var configuredPath = options?.Value?.DatabasePath ?? "Data/logs.db";

        _dbPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

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

        }
    }

    public async Task<(List<LogEntry> Entries, int TotalCount)> GetLogsAsync(
        string? level = null,
        string? keyword = null,
        string? audience = null,
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

                if (!string.IsNullOrWhiteSpace(level))
                {
                    whereConditions.Add("l.Level = @level");
                    parameters.Add(new SqliteParameter("@level", level));
                }

                if (!string.IsNullOrWhiteSpace(keyword))
                {

                    whereConditions.Add("l.Id IN (SELECT rowid FROM LogsFts WHERE LogsFts MATCH @keyword)");

                    var escapedKeyword = keyword.Replace("\"", "\"\"");
                    parameters.Add(new SqliteParameter("@keyword", escapedKeyword));
                }

                var whereClause = whereConditions.Count > 0
                    ? "WHERE " + string.Join(" AND ", whereConditions)
                    : "";

                var normalizedAudience = NormalizeAudience(audience);
                if (normalizedAudience is not null)
                {
                    whereConditions.Add(normalizedAudience == LogAudiences.Operator
                        ? $"({OperatorAudienceSql})"
                        : $"NOT ({OperatorAudienceSql})");
                    whereClause = "WHERE " + string.Join(" AND ", whereConditions);
                }

                var countSql = $@"
                SELECT COUNT(*)
                FROM Logs l
                {whereClause}
            ";

                int totalCount;
                {
                    using var countCommand = new SqliteCommand(countSql, _connection);
                    foreach (var param in parameters)
                    {
                        countCommand.Parameters.Add(param);
                    }
                    totalCount = Convert.ToInt32(countCommand.ExecuteScalar());
                }

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

                        var classification = LogAudienceClassifier.Classify(rowLevel, source);
                        entries.Add(new LogEntry
                        {
                            Timestamp = rowTimestamp,
                            Level = rowLevel,
                            Source = source,
                            Message = message,
                            Exception = exception,
                            Audience = classification.Audience,
                            Category = classification.Category
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

    private static string? NormalizeAudience(string? audience)
        => audience?.Trim().ToLowerInvariant() switch
        {
            LogAudiences.Operator => LogAudiences.Operator,
            LogAudiences.System => LogAudiences.System,
            _ => null
        };

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
