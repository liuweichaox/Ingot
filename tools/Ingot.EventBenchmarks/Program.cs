using System.Diagnostics;
using System.Globalization;
using Ingot.Domain.Events;
using Ingot.Edge.Infrastructure.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var settings = BenchmarkSettings.Parse(args);
var databasePath = settings.DatabasePath ??
                   Path.Combine(Path.GetTempPath(), $"ingot-event-benchmark-{Guid.NewGuid():N}.db");

try
{
    Console.WriteLine($"Database: {databasePath}");
    Console.WriteLine($"Rows: {settings.Rows:N0}, query samples: {settings.QuerySamples}, append samples: {settings.AppendSamples}");

    var eventLog = new SqliteEventLog(
        Options.Create(new EventOptions
        {
            DatabasePath = databasePath,
            RetentionDays = 0,
            MaxBacklogRows = checked(settings.Rows + settings.AppendSamples + 100)
        }),
        NullLogger<SqliteEventLog>.Instance);
    var baselineMemory = CaptureMemory();

    var seedStopwatch = Stopwatch.StartNew();
    await SeedAsync(databasePath, settings.Rows);
    seedStopwatch.Stop();
    Console.WriteLine($"Seed: {seedStopwatch.Elapsed.TotalSeconds:F2}s ({settings.Rows / seedStopwatch.Elapsed.TotalSeconds:N0} rows/s)");

    var appendLatencies = await MeasureAppendAsync(eventLog, settings.AppendSamples);
    var queryLatencies = await MeasureQueriesAsync(eventLog, settings.QuerySamples);
    var finalMemory = CaptureMemory();
    var managedDeltaMb = Math.Max(0, finalMemory.ManagedMb - baselineMemory.ManagedMb);
    var privateDeltaMb = Math.Max(0, finalMemory.PrivateMb - baselineMemory.PrivateMb);
    var workingSetDeltaMb = Math.Max(0, finalMemory.WorkingSetMb - baselineMemory.WorkingSetMb);
    var privateMemoryAvailable = baselineMemory.PrivateMb > 0 || finalMemory.PrivateMb > 0;

    var appendP99 = Percentile(appendLatencies, 0.99);
    var queryP95 = Percentile(queryLatencies, 0.95);
    Console.WriteLine($"Append latency: P50={Percentile(appendLatencies, 0.50):F3}ms, P95={Percentile(appendLatencies, 0.95):F3}ms, P99={appendP99:F3}ms");
    Console.WriteLine($"Indexed query latency: P50={Percentile(queryLatencies, 0.50):F3}ms, P95={queryP95:F3}ms, P99={Percentile(queryLatencies, 0.99):F3}ms");
    Console.WriteLine(
        $"Memory growth: managed={managedDeltaMb:F1}MB, " +
        $"private={(privateMemoryAvailable ? $"{privateDeltaMb:F1}MB" : "unavailable")}, " +
        $"working-set={workingSetDeltaMb:F1}MB");
    Console.WriteLine(
        $"Memory final: managed={finalMemory.ManagedMb:F1}MB, " +
        $"private={(privateMemoryAvailable ? $"{finalMemory.PrivateMb:F1}MB" : "unavailable")}, " +
        $"working-set={finalMemory.WorkingSetMb:F1}MB");

    var memoryPass = managedDeltaMb < settings.MemoryLimitMb &&
                     workingSetDeltaMb < settings.MemoryLimitMb &&
                     (!privateMemoryAvailable || privateDeltaMb < settings.MemoryLimitMb);
    var appendPass = appendP99 < 50;
    var queryPass = queryP95 < 100;
    Console.WriteLine(
        $"NFR1 event-plane memory growth < {settings.MemoryLimitMb:F0}MB: {(memoryPass ? "PASS" : "FAIL")}");
    Console.WriteLine($"NFR2 append P99 < 50ms: {(appendPass ? "PASS" : "FAIL")}");
    Console.WriteLine($"NFR5 query P95 < 100ms: {(queryPass ? "PASS" : "FAIL")}");

    if (settings.Enforce && (!memoryPass || !appendPass || !queryPass))
        return 1;
    return 0;
}
finally
{
    if (!settings.KeepDatabase)
        DeleteSqliteFiles(databasePath);
}

static async Task SeedAsync(string databasePath, int rows)
{
    if (rows <= 0)
        return;

    await using var connection = new SqliteConnection($"Data Source={databasePath}");
    await connection.OpenAsync();
    await using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync();
    }

    const int batchSize = 10_000;
    for (var batchStart = 1; batchStart <= rows; batchStart += batchSize)
    {
        var batchEnd = Math.Min(rows, batchStart + batchSize - 1);
        await using var transaction = await connection.BeginTransactionAsync();
        await using var eventCommand = connection.CreateCommand();
        eventCommand.Transaction = (SqliteTransaction)transaction;
        eventCommand.CommandText = """
                                   INSERT INTO events(
                                     event_id, event_type, type_version, occurred_at, recorded_at,
                                     source, subject_type, subject_id, correlation_id,
                                     context_json, data_json, ship_state, ship_attempts)
                                   VALUES (
                                     $event_id, $event_type, 1, $occurred_at, $recorded_at,
                                     $source, 'equipment', $subject_id, $correlation_id,
                                     $context_json, $data_json, 1, 0);
                                   SELECT last_insert_rowid();
                                   """;
        var eventId = eventCommand.Parameters.Add("$event_id", SqliteType.Text);
        var eventType = eventCommand.Parameters.Add("$event_type", SqliteType.Text);
        var occurredAt = eventCommand.Parameters.Add("$occurred_at", SqliteType.Text);
        var recordedAt = eventCommand.Parameters.Add("$recorded_at", SqliteType.Text);
        var source = eventCommand.Parameters.Add("$source", SqliteType.Text);
        var subjectId = eventCommand.Parameters.Add("$subject_id", SqliteType.Text);
        var correlationId = eventCommand.Parameters.Add("$correlation_id", SqliteType.Text);
        var contextJson = eventCommand.Parameters.Add("$context_json", SqliteType.Text);
        var dataJson = eventCommand.Parameters.Add("$data_json", SqliteType.Text);

        await using var contextCommand = connection.CreateCommand();
        contextCommand.Transaction = (SqliteTransaction)transaction;
        contextCommand.CommandText = """
                                     INSERT INTO event_context(event_seq, ctx_key, ctx_value)
                                     VALUES ($event_seq, 'material_lot', $material_lot);
                                     """;
        var eventSeq = contextCommand.Parameters.Add("$event_seq", SqliteType.Integer);
        var materialLot = contextCommand.Parameters.Add("$material_lot", SqliteType.Text);

        for (var index = batchStart; index <= batchEnd; index++)
        {
            var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(index).ToString("O");
            var lot = $"LOT-{index % 1000:000}";
            eventId.Value = $"seed-{index:D12}";
            eventType.Value = index % 2 == 0 ? "cycle.completed" : "cycle.started";
            occurredAt.Value = timestamp;
            recordedAt.Value = timestamp;
            source.Value = $"edge/BENCH/SOURCE-{index % 10:00}/cycle";
            subjectId.Value = $"EQ-{index % 100:000}";
            correlationId.Value = $"cycle-{index / 2:D12}";
            contextJson.Value = $$"""{"material_lot":"{{lot}}","tooling":"TOOL-{{index % 50:00}}"}""";
            dataJson.Value = $$"""{"good_count":{{index % 100}}}""";
            var seq = Convert.ToInt64(
                await eventCommand.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture);

            eventSeq.Value = seq;
            materialLot.Value = lot;
            await contextCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}

static async Task<double[]> MeasureAppendAsync(SqliteEventLog eventLog, int samples)
{
    var values = new double[Math.Max(1, samples)];
    for (var index = 0; index < values.Length; index++)
    {
        var evt = ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/BENCH/SOURCE-00/append",
            new ObjectRef("equipment", "EQ-BENCH"),
            $"append-{index:D8}",
            new Dictionary<string, string>
            {
                ["material_lot"] = "LOT-BENCH",
                ["tooling"] = "TOOL-BENCH"
            },
            new Dictionary<string, object?> { ["good_count"] = index });
        var stopwatch = Stopwatch.StartNew();
        await eventLog.AppendAsync(evt);
        stopwatch.Stop();
        values[index] = stopwatch.Elapsed.TotalMilliseconds;
    }

    return values;
}

static async Task<double[]> MeasureQueriesAsync(SqliteEventLog eventLog, int samples)
{
    var query = new EventQuery
    {
        EventType = "cycle.completed",
        SubjectType = "equipment",
        Context = new Dictionary<string, string> { ["material_lot"] = "LOT-500" },
        Limit = 100
    };

    for (var warmup = 0; warmup < 10; warmup++)
        await eventLog.QueryAsync(query);

    var values = new double[Math.Max(1, samples)];
    for (var index = 0; index < values.Length; index++)
    {
        var stopwatch = Stopwatch.StartNew();
        var events = await eventLog.QueryAsync(query);
        stopwatch.Stop();
        if (events.Count == 0)
            throw new InvalidOperationException("Benchmark query returned no rows.");
        values[index] = stopwatch.Elapsed.TotalMilliseconds;
    }

    return values;
}

static double Percentile(IReadOnlyCollection<double> source, double percentile)
{
    var ordered = source.Order().ToArray();
    var index = Math.Clamp(
        (int)Math.Ceiling(percentile * ordered.Length) - 1,
        0,
        ordered.Length - 1);
    return ordered[index];
}

static MemorySnapshot CaptureMemory()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    using var process = Process.GetCurrentProcess();
    process.Refresh();
    const double bytesPerMegabyte = 1024d * 1024d;
    return new MemorySnapshot(
        GC.GetTotalMemory(forceFullCollection: false) / bytesPerMegabyte,
        process.PrivateMemorySize64 / bytesPerMegabyte,
        process.WorkingSet64 / bytesPerMegabyte);
}

static void DeleteSqliteFiles(string path)
{
    foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
    {
        if (File.Exists(candidate))
            File.Delete(candidate);
    }
}

internal sealed record BenchmarkSettings
{
    public int Rows { get; init; } = 100_000;
    public int QuerySamples { get; init; } = 100;
    public int AppendSamples { get; init; } = 1_000;
    public double MemoryLimitMb { get; init; } = 50;
    public string? DatabasePath { get; init; }
    public bool KeepDatabase { get; init; }
    public bool Enforce { get; init; }

    public static BenchmarkSettings Parse(string[] args)
    {
        var settings = new BenchmarkSettings();
        for (var index = 0; index < args.Length; index++)
        {
            settings = args[index] switch
            {
                "--rows" => settings with { Rows = ParsePositive(args, ref index, "--rows") },
                "--query-samples" => settings with
                {
                    QuerySamples = ParsePositive(args, ref index, "--query-samples")
                },
                "--append-samples" => settings with
                {
                    AppendSamples = ParsePositive(args, ref index, "--append-samples")
                },
                "--memory-limit-mb" => settings with
                {
                    MemoryLimitMb = ParsePositiveDouble(args, ref index, "--memory-limit-mb")
                },
                "--database" => settings with { DatabasePath = ReadValue(args, ref index, "--database") },
                "--keep" => settings with { KeepDatabase = true },
                "--enforce" => settings with { Enforce = true },
                _ => throw new ArgumentException($"Unknown option: {args[index]}")
            };
        }

        return settings;
    }

    private static int ParsePositive(string[] args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentException($"{option} must be a positive integer.");
        return parsed;
    }

    private static double ParsePositiveDouble(string[] args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed <= 0)
            throw new ArgumentException($"{option} must be a positive number.");
        return parsed;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }
}

internal readonly record struct MemorySnapshot(
    double ManagedMb,
    double PrivateMb,
    double WorkingSetMb);
