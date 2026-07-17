using System;
using System.IO;
using Ingot.Infrastructure.Logs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class MicrosoftSqliteSinkTests
{
    [Fact]
    public void Constructor_ShouldDeleteLogsOlderThanRetentionDays()
    {
        var dbPath = CreateTempDbPath();

        try
        {
            SeedLogs(
                dbPath,
                (DateTimeOffset.UtcNow.AddDays(-31), "expired"),
                (DateTimeOffset.UtcNow.AddDays(-29), "kept"));

            using (var sink = new MicrosoftSqliteSink(dbPath, retentionDays: 30))
            {
            }

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var command = new SqliteCommand("SELECT Message FROM Logs ORDER BY Id", connection);
            using var reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal("kept", reader.GetString(0));
            Assert.False(reader.Read());
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
        }
    }

    [Fact]
    public void Constructor_ShouldSkipCleanupWhenRetentionIsDisabled()
    {
        var dbPath = CreateTempDbPath();

        try
        {
            SeedLogs(
                dbPath,
                (DateTimeOffset.UtcNow.AddDays(-90), "old-1"),
                (DateTimeOffset.UtcNow.AddDays(-60), "old-2"));

            using (var sink = new MicrosoftSqliteSink(dbPath, retentionDays: 0))
            {
            }

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var command = new SqliteCommand("SELECT COUNT(*) FROM Logs", connection);
            var count = Convert.ToInt32(command.ExecuteScalar());

            Assert.Equal(2, count);
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
        }
    }

    [Fact]
    public void LogOptions_ShouldUse30DaysRetentionByDefault()
    {
        var options = new LogOptions();

        Assert.Equal(30, options.RetentionDays);
    }

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}.db");
    }

    private static void SeedLogs(string dbPath, params (DateTimeOffset Timestamp, string Message)[] logs)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var createCommand = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS Logs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeStamp TEXT NOT NULL,
                Level TEXT NOT NULL,
                Message TEXT NOT NULL,
                MessageTemplate TEXT,
                Exception TEXT,
                Properties TEXT
            );
        ", connection);
        createCommand.ExecuteNonQuery();

        foreach (var log in logs)
        {
            using var insertCommand = new SqliteCommand(@"
                INSERT INTO Logs (TimeStamp, Level, Message, MessageTemplate, Exception, Properties)
                VALUES (@timestamp, 'Information', @message, @messageTemplate, NULL, NULL);
            ", connection);
            insertCommand.Parameters.AddWithValue("@timestamp", log.Timestamp.ToString("O"));
            insertCommand.Parameters.AddWithValue("@message", log.Message);
            insertCommand.Parameters.AddWithValue("@messageTemplate", log.Message);
            insertCommand.ExecuteNonQuery();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup failures in temp path
        }
    }
}
