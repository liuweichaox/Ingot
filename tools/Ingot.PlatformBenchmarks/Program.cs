// 运行平台基准场景并输出可比较的性能测量结果。

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;

var settings = Settings.Parse(args);
using var http = new HttpClient
{
    BaseAddress = new Uri(settings.PlatformUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(60)
};
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
if (settings.EventShape == EventShapes.ProcessSample)
    await EnsureProcessSampleConfigurationAsync(http, settings, jsonOptions);

var startedAt = DateTimeOffset.UtcNow;
var stopwatch = Stopwatch.StartNew();
var accepted = 0;
var duplicates = 0;

for (var first = 1; first <= settings.Events; first += settings.BatchSize)
{
    var last = Math.Min(settings.Events, first + settings.BatchSize - 1);
    var batch = new EventBatchRequest
    {
        SiteId = settings.SiteId,
        EdgeId = settings.EdgeId,
        Events = Enumerable.Range(first, last - first + 1)
            .Select(index => CreateEvent(settings, index))
            .ToArray()
    };
    using var response = await http.PostAsJsonAsync(
        "api/v1/events:batch",
        batch,
        jsonOptions);
    response.EnsureSuccessStatusCode();
    var confirmation = await response.Content.ReadFromJsonAsync<EventBatchResponse>(jsonOptions)
                       ?? throw new InvalidDataException("Platform returned an empty acknowledgement.");
    if (confirmation.AckSeq != last)
        throw new InvalidDataException(
            $"Unexpected AckSeq. Expected={last}, Actual={confirmation.AckSeq}");
    if (confirmation.GapDetected)
        throw new InvalidDataException($"Platform detected an unexpected sequence gap at batch {first}-{last}.");
    accepted += confirmation.Accepted;
    duplicates += confirmation.Duplicates;
}

stopwatch.Stop();
var eventsPerSecond = settings.Events / stopwatch.Elapsed.TotalSeconds;
Console.WriteLine($"Platform: {settings.PlatformUrl}");
Console.WriteLine($"Site: {settings.SiteId}");
Console.WriteLine($"Edge: {settings.EdgeId}");
Console.WriteLine($"Started UTC: {startedAt:O}");
Console.WriteLine($"Shape: {settings.EventShape}");
Console.WriteLine($"Events: {settings.Events:N0}, batch size: {settings.BatchSize}");
if (settings.EventShape == EventShapes.ProcessSample)
{
    Console.WriteLine($"Signals/event: {settings.SignalCount:N0}");
    Console.WriteLine($"Expected sample frames: {settings.Events:N0}");
    Console.WriteLine($"Expected typed value rows: {(long)settings.Events * settings.SignalCount:N0}");
}
Console.WriteLine($"Accepted: {accepted:N0}, duplicates: {duplicates:N0}");
Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F3}s");
Console.WriteLine($"Throughput: {eventsPerSecond:N0} events/s");
if (settings.Enforce)
{
    Console.WriteLine($"NFR6 platform ingest >= {settings.MinimumEventsPerSecond:N0} events/s: " +
                      $"{(eventsPerSecond >= settings.MinimumEventsPerSecond ? "PASS" : "FAIL")}");
}
else
{
    Console.WriteLine("Rate enforcement: disabled (measurement only)");
}

return settings.Enforce && eventsPerSecond < settings.MinimumEventsPerSecond ? 1 : 0;

static ProductionEvent CreateEvent(Settings settings, int seq)
    => settings.EventShape == EventShapes.ProcessSample
        ? CreateProcessSampleEvent(settings, seq)
        : CreateLifecycleEvent(settings.EdgeId, seq);

static ProductionEvent CreateLifecycleEvent(string edgeId, int seq)
{
    var timestamp = DateTimeOffset.UtcNow;
    return ProductionEvent.Create(
        seq % 2 == 0 ? "process.execution.completed" : "process.execution.started",
        timestamp,
        $"edge/{edgeId}/BENCH-SOURCE/execution",
        new ObjectRef("equipment", $"EQ-{seq % 100:000}"),
        $"execution-{(seq + 1) / 2:D12}",
        new Dictionary<string, string>
        {
            ["material_lot"] = $"LOT-{seq % 1000:000}",
            ["tooling"] = $"TOOL-{seq % 50:00}",
            ["acceptance_run"] = edgeId
        },
        new Dictionary<string, object?>
        {
            ["good_count"] = seq % 100,
            ["benchmark"] = true
        }) with
    {
        Seq = seq
    };
}

static ProductionEvent CreateProcessSampleEvent(Settings settings, int seq)
{
    var timestamp = DateTimeOffset.UtcNow;
    var executionOrdinal = ((seq - 1) / settings.SamplesPerExecution) + 1;
    var sampleOrdinal = ((seq - 1) % settings.SamplesPerExecution) + 1;
    var executionId = $"{settings.EdgeId}-execution-{executionOrdinal:D8}";
    var values = Enumerable.Range(1, settings.SignalCount)
        .ToDictionary(
            static signal => $"signal_{signal:00}",
            signal => (object?)(signal * 10d + Math.Sin((sampleOrdinal + signal) / 20d)),
            StringComparer.Ordinal);

    return ProductionEvent.Create(
        "process.sample",
        timestamp,
        $"edge/{settings.EdgeId}/BENCH-SOURCE/sample",
        new ObjectRef("equipment", "BENCH-EQUIPMENT-001"),
        executionId,
        new Dictionary<string, string>
        {
            ["data_model_id"] = settings.DataModelId,
            ["data_model_version"] = "1",
            ["product_family_code"] = "BENCH-PRODUCT",
            ["equipment_id"] = "BENCH-EQUIPMENT-001",
            ["acceptance_run"] = settings.EdgeId
        },
        new Dictionary<string, object?>
        {
            ["values"] = values,
            ["sourceSequence"] = sampleOrdinal,
            ["benchmark"] = true
        }) with
    {
        Seq = seq
    };
}

static async Task EnsureProcessSampleConfigurationAsync(
    HttpClient http,
    Settings settings,
    JsonSerializerOptions jsonOptions)
{
    var now = DateTimeOffset.UtcNow;
    var items = Enumerable.Range(1, settings.SignalCount)
        .Select(static signal => new ProcessDataItemDefinition
        {
            Code = $"signal_{signal:00}",
            DisplayName = $"Benchmark signal {signal:00}",
            DataType = "double",
            Unit = "1",
            Category = "process",
            Nullable = false
        })
        .ToArray();
    var model = new ProcessDataModel
    {
        ModelId = settings.DataModelId,
        Version = 1,
        Name = "Platform process-sample benchmark",
        Status = ConfigurationStatuses.Published,
        Acquisition = new AcquisitionModel { DataItems = items },
        UpdatedAt = now
    };
    var plan = new ProcessAnalysisPlan
    {
        PlanId = settings.AnalysisPlanId,
        Version = 1,
        Name = "Platform process-sample benchmark",
        Status = ConfigurationStatuses.Published,
        DataModelId = settings.DataModelId,
        DataModelVersion = 1,
        AnalysisScope = "production-execution",
        AlignmentMode = "elapsed",
        ComparisonKeys = ["product_family_code"],
        Signals = items.Select(static item => new AnalysisSignalSelection
        {
            DataItemCode = item.Code,
            Features = ["mean", "min", "max"]
        }).ToArray(),
        UpdatedAt = now
    };

    using var modelResponse = await http.PostAsJsonAsync(
        "api/v1/process-data-models",
        model,
        jsonOptions);
    modelResponse.EnsureSuccessStatusCode();
    using var planResponse = await http.PostAsJsonAsync(
        "api/v1/process-analysis-plans",
        plan,
        jsonOptions);
    planResponse.EnsureSuccessStatusCode();
}

internal static class EventShapes
{
    public const string Lifecycle = "lifecycle";
    public const string ProcessSample = "process-sample";

    public static bool IsValid(string value)
        => value is Lifecycle or ProcessSample;
}

internal sealed record Settings
{
    public string PlatformUrl { get; init; } = "http://127.0.0.1:18080";
    public string SiteId { get; init; } = "SITE-BENCHMARK";
    public string EdgeId { get; init; } = $"BENCH-{Guid.NewGuid():N}";
    public string Token { get; init; } = "benchmark-token";
    public int Events { get; init; } = 10_000;
    public int BatchSize { get; init; } = 500;
    public string EventShape { get; init; } = EventShapes.Lifecycle;
    public int SignalCount { get; init; } = 15;
    public int SamplesPerExecution { get; init; } = 1_000;
    public double MinimumEventsPerSecond { get; init; } = 500;
    public bool Enforce { get; init; }
    public string DataModelId => $"benchmark-{EdgeId.ToLowerInvariant()}";
    public string AnalysisPlanId => $"benchmark-{EdgeId.ToLowerInvariant()}";

    public static Settings Parse(string[] args)
    {
        var settings = new Settings();
        for (var index = 0; index < args.Length; index++)
        {
            settings = args[index] switch
            {
                "--platform-url" => settings with
                {
                    PlatformUrl = ReadValue(args, ref index, "--platform-url")
                },
                "--edge-id" => settings with { EdgeId = ReadValue(args, ref index, "--edge-id") },
                "--site-id" => settings with { SiteId = ReadValue(args, ref index, "--site-id") },
                "--token" => settings with { Token = ReadValue(args, ref index, "--token") },
                "--events" => settings with
                {
                    Events = ParsePositive(args, ref index, "--events")
                },
                "--batch-size" => settings with
                {
                    BatchSize = Math.Clamp(ParsePositive(args, ref index, "--batch-size"), 1, 500)
                },
                "--shape" => settings with
                {
                    EventShape = ParseShape(args, ref index)
                },
                "--signals" => settings with
                {
                    SignalCount = Math.Clamp(ParsePositive(args, ref index, "--signals"), 1, 256)
                },
                "--samples-per-execution" => settings with
                {
                    SamplesPerExecution = ParsePositive(args, ref index, "--samples-per-execution")
                },
                "--minimum-rate" => settings with
                {
                    MinimumEventsPerSecond = ParsePositiveDouble(args, ref index, "--minimum-rate")
                },
                "--enforce" => settings with { Enforce = true },
                _ => throw new ArgumentException($"Unknown option: {args[index]}")
            };
        }

        return settings;
    }

    private static string ParseShape(string[] args, ref int index)
    {
        var value = ReadValue(args, ref index, "--shape").Trim().ToLowerInvariant();
        return EventShapes.IsValid(value)
            ? value
            : throw new ArgumentException("--shape must be 'lifecycle' or 'process-sample'.");
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
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
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
