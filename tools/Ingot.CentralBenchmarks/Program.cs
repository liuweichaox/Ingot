using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;

var settings = Settings.Parse(args);
using var http = new HttpClient
{
    BaseAddress = new Uri(settings.CentralUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(60)
};
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var stopwatch = Stopwatch.StartNew();
var accepted = 0;
var duplicates = 0;

for (var first = 1; first <= settings.Events; first += settings.BatchSize)
{
    var last = Math.Min(settings.Events, first + settings.BatchSize - 1);
    var batch = new EventBatchRequest
    {
        EdgeId = settings.EdgeId,
        Events = Enumerable.Range(first, last - first + 1)
            .Select(index => CreateEvent(settings.EdgeId, index))
            .ToArray()
    };
    using var response = await http.PostAsJsonAsync(
        "api/v1/events:batch",
        batch,
        jsonOptions);
    response.EnsureSuccessStatusCode();
    var confirmation = await response.Content.ReadFromJsonAsync<EventBatchResponse>(jsonOptions)
                       ?? throw new InvalidDataException("Central returned an empty acknowledgement.");
    if (confirmation.AckSeq != last)
        throw new InvalidDataException(
            $"Unexpected AckSeq. Expected={last}, Actual={confirmation.AckSeq}");
    if (confirmation.GapDetected)
        throw new InvalidDataException($"Central detected an unexpected sequence gap at batch {first}-{last}.");
    accepted += confirmation.Accepted;
    duplicates += confirmation.Duplicates;
}

stopwatch.Stop();
var eventsPerSecond = settings.Events / stopwatch.Elapsed.TotalSeconds;
Console.WriteLine($"Central: {settings.CentralUrl}");
Console.WriteLine($"Edge: {settings.EdgeId}");
Console.WriteLine($"Events: {settings.Events:N0}, batch size: {settings.BatchSize}");
Console.WriteLine($"Accepted: {accepted:N0}, duplicates: {duplicates:N0}");
Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F3}s");
Console.WriteLine($"Throughput: {eventsPerSecond:N0} events/s");
Console.WriteLine($"NFR6 central ingest >= {settings.MinimumEventsPerSecond:N0} events/s: " +
                  $"{(eventsPerSecond >= settings.MinimumEventsPerSecond ? "PASS" : "FAIL")}");

return settings.Enforce && eventsPerSecond < settings.MinimumEventsPerSecond ? 1 : 0;

static ProductionEvent CreateEvent(string edgeId, int seq)
{
    var timestamp = DateTimeOffset.UtcNow;
    return ProductionEvent.Create(
        seq % 2 == 0 ? "cycle.completed" : "cycle.started",
        timestamp,
        $"edge/{edgeId}/BENCH-SOURCE/cycle",
        new ObjectRef("equipment", $"EQ-{seq % 100:000}"),
        $"cycle-{(seq + 1) / 2:D12}",
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

internal sealed record Settings
{
    public string CentralUrl { get; init; } = "http://127.0.0.1:18080";
    public string EdgeId { get; init; } = $"BENCH-{Guid.NewGuid():N}";
    public string Token { get; init; } = "benchmark-token";
    public int Events { get; init; } = 10_000;
    public int BatchSize { get; init; } = 500;
    public double MinimumEventsPerSecond { get; init; } = 500;
    public bool Enforce { get; init; }

    public static Settings Parse(string[] args)
    {
        var settings = new Settings();
        for (var index = 0; index < args.Length; index++)
        {
            settings = args[index] switch
            {
                "--central-url" => settings with
                {
                    CentralUrl = ReadValue(args, ref index, "--central-url")
                },
                "--edge-id" => settings with { EdgeId = ReadValue(args, ref index, "--edge-id") },
                "--token" => settings with { Token = ReadValue(args, ref index, "--token") },
                "--events" => settings with
                {
                    Events = ParsePositive(args, ref index, "--events")
                },
                "--batch-size" => settings with
                {
                    BatchSize = Math.Clamp(ParsePositive(args, ref index, "--batch-size"), 1, 500)
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
