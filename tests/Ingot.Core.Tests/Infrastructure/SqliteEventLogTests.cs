using Ingot.Domain.Events;
using Ingot.Infrastructure.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class SqliteEventLogTests
{
    [Fact]
    public async Task Append_ShouldSurviveReopenAndSupportBusinessFilters()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var options = Options.Create(new EventOptions
            {
                DatabasePath = dbPath,
                MaxBacklogRows = 100
            });
            var firstLog = new SqliteEventLog(options, NullLogger<SqliteEventLog>.Instance);

            var firstSeq = await firstLog.AppendAsync(CreateEvent(
                "cycle.started",
                "cycle-01",
                "LOT-A"));
            var secondSeq = await firstLog.AppendAsync(CreateEvent(
                "cycle.completed",
                "cycle-01",
                "LOT-A"));
            await firstLog.AppendAsync(CreateEvent(
                "cycle.completed",
                "cycle-02",
                "LOT-B"));

            Assert.Equal(1, firstSeq);
            Assert.Equal(2, secondSeq);

            var reopened = new SqliteEventLog(options, NullLogger<SqliteEventLog>.Instance);
            var results = await reopened.QueryAsync(new EventQuery
            {
                EventType = "cycle.completed",
                SubjectType = "equipment",
                SubjectId = "POL-03",
                CorrelationId = "cycle-01",
                Context = new Dictionary<string, string>
                {
                    ["material_lot"] = "LOT-A"
                }
            });

            var evt = Assert.Single(results);
            Assert.Equal(secondSeq, evt.Seq);
            Assert.Equal("cycle.completed", evt.EventType);
            Assert.Equal("LOT-A", evt.Context["material_lot"]);
            Assert.Equal(2, Assert.IsType<System.Text.Json.JsonElement>(evt.Data["count"]).GetInt32());
            Assert.Equal(3, await reopened.CountPendingAsync());
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task MarkShipped_ShouldAdvanceOutboxWithoutDeletingFacts()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var log = new SqliteEventLog(
                Options.Create(new EventOptions
                {
                    DatabasePath = dbPath,
                    RetentionDays = 7,
                    MaxBacklogRows = 100
                }),
                NullLogger<SqliteEventLog>.Instance);

            await log.AppendAsync(CreateEvent("cycle.started", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.completed", "cycle-01", "LOT-A"));

            await log.MarkShippedAsync(1);

            Assert.Equal(1, await log.CountPendingAsync());
            Assert.Single(await log.ReadPendingAsync(100));
            Assert.Equal(2, (await log.QueryAsync(new EventQuery { Limit = 100 })).Count);
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task QueryAfterSeq_ShouldReturnAscendingCursorOrder()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var log = new SqliteEventLog(
                Options.Create(new EventOptions { DatabasePath = dbPath }),
                NullLogger<SqliteEventLog>.Instance);
            await log.AppendAsync(CreateEvent("cycle.started", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.completed", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("alarm.raised", "alarm-01", "LOT-A"));

            var results = await log.QueryAsync(new EventQuery { AfterSeq = 1, Limit = 100 });

            Assert.Equal([2L, 3L], results.Select(static evt => evt.Seq));
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task BacklogLimit_ShouldKeepExplicitDiagnosticInsideBoundedOutbox()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var log = new SqliteEventLog(
                Options.Create(new EventOptions
                {
                    DatabasePath = dbPath,
                    MaxBacklogRows = 3
                }),
                NullLogger<SqliteEventLog>.Instance);

            await log.AppendAsync(CreateEvent("cycle.started", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.completed", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.started", "cycle-02", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.completed", "cycle-02", "LOT-A"));

            Assert.Equal(3, await log.CountPendingAsync());
            var all = await log.QueryAsync(new EventQuery { Limit = 100 });
            var diagnostic = Assert.Single(all, evt => evt.EventType == "diagnostic.backlog_dropped");
            Assert.Equal("system", diagnostic.Subject.Type);
            Assert.Equal("event-outbox", diagnostic.Subject.Id);
            Assert.Equal(
                2,
                Assert.IsType<System.Text.Json.JsonElement>(diagnostic.Data["dropped_count"]).GetInt32());
            Assert.DoesNotContain(all, evt => evt.Seq is 1 or 2);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await using var orphanCommand = connection.CreateCommand();
            orphanCommand.CommandText = """
                                        SELECT COUNT(*)
                                        FROM event_context AS context
                                        LEFT JOIN events ON events.seq = context.event_seq
                                        WHERE events.seq IS NULL;
                                        """;
            Assert.Equal(0L, (long)(await orphanCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task IncrementShipAttempts_ShouldPersistRetryAudit()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var log = new SqliteEventLog(
                Options.Create(new EventOptions { DatabasePath = dbPath }),
                NullLogger<SqliteEventLog>.Instance);
            await log.AppendAsync(CreateEvent("cycle.started", "cycle-01", "LOT-A"));
            await log.AppendAsync(CreateEvent("cycle.completed", "cycle-01", "LOT-A"));

            await log.IncrementShipAttemptsAsync(1, 2);
            await log.IncrementShipAttemptsAsync(1, 1);

            await using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT seq, ship_attempts FROM events ORDER BY seq;";
            await using var reader = await command.ExecuteReaderAsync();
            var attempts = new List<(long Seq, long Attempts)>();
            while (await reader.ReadAsync())
                attempts.Add((reader.GetInt64(0), reader.GetInt64(1)));

            Assert.Equal([(1L, 2L), (2L, 1L)], attempts);
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    [Fact]
    public async Task MarkShipped_ShouldApplyRetentionDuringLongRunningProcess()
    {
        var dbPath = CreateTempDbPath();
        try
        {
            var log = new SqliteEventLog(
                Options.Create(new EventOptions
                {
                    DatabasePath = dbPath,
                    RetentionDays = 7,
                    CleanupIntervalSeconds = 0
                }),
                NullLogger<SqliteEventLog>.Instance);
            var oldEvent = CreateEvent("cycle.completed", "cycle-old", "LOT-OLD") with
            {
                OccurredAt = DateTimeOffset.UtcNow.AddDays(-10),
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-10)
            };
            await log.AppendAsync(oldEvent);

            await log.MarkShippedAsync(1);

            Assert.Empty(await log.QueryAsync(new EventQuery { Limit = 100 }));
        }
        finally
        {
            DeleteSqliteFiles(dbPath);
        }
    }

    private static ProductionEvent CreateEvent(string type, string correlationId, string lot)
        => ProductionEvent.Create(
            type,
            DateTimeOffset.UtcNow,
            "edge/EDGE-01/PLC-01/rule-01",
            new ObjectRef("equipment", "POL-03"),
            correlationId,
            new Dictionary<string, string> { ["material_lot"] = lot },
            new Dictionary<string, object?> { ["count"] = 2 });

    private static string CreateTempDbPath()
        => Path.Combine(Path.GetTempPath(), $"ingot-events-{Guid.NewGuid():N}.db");

    private static void DeleteSqliteFiles(string dbPath)
    {
        foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temporary test cleanup is best-effort.
            }
        }
    }
}
