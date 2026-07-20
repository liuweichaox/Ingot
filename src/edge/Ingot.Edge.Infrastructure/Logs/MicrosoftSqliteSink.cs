using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog.Core;
using Serilog.Events;

namespace Ingot.Edge.Infrastructure.Logs;

/// <summary>
///     Serilog sink backed by Microsoft.Data.Sqlite.
/// </summary>
public sealed class MicrosoftSqliteSink : ILogEventSink, IDisposable
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Queue<LogEvent> _eventQueue = new();
    private readonly Timer _flushTimer;
    private readonly int _batchSize;
    private readonly int _retentionDays;
    private readonly TimeSpan _cleanupInterval;
    private SqliteConnection? _connection;
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;

    public MicrosoftSqliteSink(
        string dbPath,
        int batchSize = 1000,
        TimeSpan? flushInterval = null,
        int retentionDays = 30,
        TimeSpan? cleanupInterval = null)
    {
        _dbPath = dbPath;
        _batchSize = batchSize;
        _retentionDays = retentionDays;
        _cleanupInterval = cleanupInterval ?? TimeSpan.FromHours(12);

        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        InitializeDatabase();
        CleanupExpiredLogs(force: true);

        var interval = flushInterval ?? TimeSpan.FromSeconds(5);
        _flushTimer = new Timer(FlushQueue, null, interval, interval);
    }

    private void InitializeDatabase()
    {
        var connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using var command = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS Logs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeStamp TEXT NOT NULL,
                Level TEXT NOT NULL,
                Message TEXT NOT NULL,
                MessageTemplate TEXT,
                Exception TEXT,
                Properties TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON Logs(TimeStamp DESC);
            CREATE INDEX IF NOT EXISTS idx_logs_level ON Logs(Level);
        ", _connection);
        command.ExecuteNonQuery();
    }

    public void Emit(LogEvent logEvent)
    {
        if (_connection == null)
        {
            return;
        }

        lock (_eventQueue)
        {
            _eventQueue.Enqueue(logEvent);

            var isCritical = logEvent.Level >= LogEventLevel.Error;
            if (_eventQueue.Count >= _batchSize || isCritical)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        FlushQueue();
                    }
                    catch
                    {
                        // Sink should not break the caller.
                    }
                });
            }
        }
    }

    private void FlushQueue(object? state = null)
    {
        FlushQueueInternal();
    }

    private void FlushQueueInternal()
    {
        if (_connection == null)
        {
            return;
        }

        _writeLock.Wait();
        try
        {
            var eventsToWrite = new List<LogEvent>();

            lock (_eventQueue)
            {
                while (_eventQueue.Count > 0 && eventsToWrite.Count < _batchSize)
                {
                    eventsToWrite.Add(_eventQueue.Dequeue());
                }
            }

            if (eventsToWrite.Count > 0)
            {
                using var transaction = _connection.BeginTransaction();
                try
                {
                    const string sql = @"
                        INSERT INTO Logs (TimeStamp, Level, Message, MessageTemplate, Exception, Properties)
                        VALUES (@timestamp, @level, @message, @messageTemplate, @exception, @properties)
                    ";

                    foreach (var logEvent in eventsToWrite)
                    {
                        using var command = new SqliteCommand(sql, _connection, transaction);
                        command.Parameters.AddWithValue("@timestamp", logEvent.Timestamp.ToString("O"));
                        command.Parameters.AddWithValue("@level", logEvent.Level.ToString());
                        command.Parameters.AddWithValue("@message", logEvent.RenderMessage());
                        command.Parameters.AddWithValue("@messageTemplate", logEvent.MessageTemplate.Text);
                        command.Parameters.AddWithValue("@exception",
                            logEvent.Exception?.ToString() ?? (object)DBNull.Value);

                        var properties = new Dictionary<string, object>();
                        foreach (var property in logEvent.Properties)
                        {
                            properties[property.Key] = property.Value.ToString();
                        }

                        var propertiesJson = properties.Count > 0
                            ? JsonSerializer.Serialize(properties)
                            : (object)DBNull.Value;
                        command.Parameters.AddWithValue("@properties", propertiesJson);
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            CleanupExpiredLogs();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void CleanupExpiredLogs(bool force = false)
    {
        if (_connection == null || _retentionDays <= 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastCleanupUtc < _cleanupInterval)
        {
            return;
        }

        using var command = new SqliteCommand(@"
            DELETE FROM Logs
            WHERE julianday(TimeStamp) < julianday(@cutoff);
        ", _connection);
        command.Parameters.AddWithValue("@cutoff", now.AddDays(-_retentionDays).ToString("O"));
        command.ExecuteNonQuery();

        _lastCleanupUtc = now;
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushQueueInternal();

        _connection?.Close();
        _connection?.Dispose();
        _writeLock.Dispose();
    }
}
