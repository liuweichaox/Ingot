using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.ImportCli;

// ingot-import：把历史 CSV 数据按映射文件转换为标准生产事件批次，写入 Platform。
// 体检（数据质量报告）由此获得"不依赖实时采集"的一等数据入口。
// 用法：
//   ingot-import --file history.csv --mapping mapping.json --url http://localhost:8000 \
//                --token $INGOT_EDGE_TOKEN [--seq-start 1] [--source-tag historical-data]
//                [--dry-run] [--show-values] [--batch 500]
// 说明：
//   - seq 单调递增即可、允许间隙；平台按 eventId 与 (edgeId, seq) 去重，失败后用相同
//     --seq-start 重跑是安全的（重复行会被识别为 duplicates）。
//   - --seq-start 缺省取启动时刻的 unix 毫秒 ×1000，保证多次导入不同文件时仍单调。
//   - 映射文件格式见 sample-mapping.json 与 README。

var arguments = ParseArgs(args);
if (arguments is null)
    return 2;

try
{
    var mappingJson = await File.ReadAllTextAsync(arguments.MappingPath);
    var mapping = MappingEngine.LoadMapping(mappingJson);

    using var fileReader = new StreamReader(arguments.FilePath);
    var events = new List<ProductionEvent>();
    var previewEvents = new List<ProductionEvent>();
    long seq = arguments.SeqStart, rows = 0, shipped = 0, duplicates = 0;

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    if (!arguments.DryRun)
    {
        http.BaseAddress = new Uri(arguments.Url!.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", arguments.Token);
    }

    foreach (var row in MappingEngine.ReadCsv(fileReader))
    {
        rows++;
        ProductionEvent evt;
        try
        {
            evt = MappingEngine.BuildEvent(row, mapping, seq++, arguments.SourceTag);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            Console.Error.WriteLine($"第 {rows} 行映射失败：{ex.Message}");
            return 1;
        }

        if (!ProductionEventValidator.TryValidate(evt, requirePersistedSequence: true, out var validationError))
        {
            Console.Error.WriteLine($"第 {rows} 行契约校验失败：{validationError}");
            return 1;
        }
        if (arguments.DryRun)
        {
            if (arguments.ShowValues && previewEvents.Count < 3)
                previewEvents.Add(evt);
            continue;
        }

        events.Add(evt);
        if (events.Count >= arguments.BatchSize)
        {
            var (accepted, dup) = await ShipAsync(http, mapping.EdgeId, events);
            shipped += accepted; duplicates += dup;
            Console.WriteLine($"已提交 {rows} 行：accepted={shipped}, duplicates={duplicates}");
            events.Clear();
        }
    }

    if (arguments.DryRun)
    {
        Console.WriteLine($"[dry-run] 已读取并验证 {rows} 行；未向平台提交数据。");
        if (arguments.ShowValues)
        {
            Console.Error.WriteLine("[dry-run] 警告：以下预览包含源数据值，只能在受控终端中使用。");
            Console.WriteLine(JsonSerializer.Serialize(
                new EventBatchRequest { EdgeId = mapping.EdgeId, Events = previewEvents },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("[dry-run] 默认不输出转换后的事件内容；确需排查时仅在受控终端明确添加 --show-values。");
        }
        Console.WriteLine("[dry-run] 校验通过。移除 --dry-run 并提供 --url/--token 后执行导入。");
        return 0;
    }

    if (events.Count > 0)
    {
        var (accepted, dup) = await ShipAsync(http, mapping.EdgeId, events);
        shipped += accepted; duplicates += dup;
    }

    Console.WriteLine($"导入完成：{rows} 行 → accepted={shipped}, duplicates={duplicates}, edgeId={mapping.EdgeId}");
    Console.WriteLine($"下一步：在 Platform Web 打开数据质量页，或调用体检接口查看该范围的覆盖与配对情况。");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"导入失败：{ex.Message}");
    return 1;
}

static async Task<(int Accepted, int Duplicates)> ShipAsync(
    HttpClient http,
    string edgeId,
    IReadOnlyList<ProductionEvent> events)
{
    var request = new EventBatchRequest { EdgeId = edgeId, Events = events };
    using var response = await http.PostAsJsonAsync(
        "api/v1/events:batch", request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException(
            $"平台拒绝批次（HTTP {(int)response.StatusCode}）：{body}\n" +
            $"失败批次 Seq 范围 {events[0].Seq}-{events[^1].Seq}；修正后用相同 --seq-start 重跑即可安全续传。");
    var result = JsonSerializer.Deserialize<EventBatchResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("平台返回了空的确认响应。");
    return (result.Accepted, result.Duplicates);
}

static CliArguments? ParseArgs(string[] args)
{
    string? file = null, mapping = null, url = null, token = null;
    var sourceTag = "historical-data";
    long? seqStart = null;
    var dryRun = false;
    var showValues = false;
    var batch = 500;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--file": file = Next(args, ref i); break;
            case "--mapping": mapping = Next(args, ref i); break;
            case "--url": url = Next(args, ref i); break;
            case "--token": token = Next(args, ref i); break;
            case "--seq-start": seqStart = long.Parse(Next(args, ref i) ?? "0"); break;
            case "--source-tag": sourceTag = Next(args, ref i) ?? ""; break;
            case "--batch": batch = Math.Clamp(int.Parse(Next(args, ref i) ?? "500"), 1, 500); break;
            case "--dry-run": dryRun = true; break;
            case "--show-values": showValues = true; break;
            case "--help" or "-h": PrintUsage(); return null;
            default:
                Console.Error.WriteLine($"未知参数：{args[i]}");
                PrintUsage();
                return null;
        }
    }
    if (file is null || mapping is null || (!dryRun && (url is null || token is null)))
    {
        PrintUsage();
        return null;
    }
    if (showValues && !dryRun)
    {
        Console.Error.WriteLine("--show-values 只能与 --dry-run 一起使用。");
        return null;
    }
    if (sourceTag.Length is < 1 or > 64 ||
        sourceTag.Any(static value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_' and not '.'))
    {
        Console.Error.WriteLine("--source-tag 只能包含 1-64 个 ASCII 字母、数字、点、短横线或下划线。");
        return null;
    }
    return new CliArguments(
        file,
        mapping,
        url,
        token,
        seqStart ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000,
        sourceTag,
        dryRun,
        showValues,
        batch);

    static string? Next(string[] args, ref int i) => ++i < args.Length ? args[i] : null;

    static void PrintUsage()
        => Console.Error.WriteLine(
            "用法: ingot-import --file <csv> --mapping <json> [--dry-run] [--show-values] " +
            "[--url <platform-url> --token <edge-token>] [--seq-start <n>] " +
            "[--source-tag <opaque-tag>] [--batch <1-500>]");
}

internal sealed record CliArguments(
    string FilePath,
    string MappingPath,
    string? Url,
    string? Token,
    long SeqStart,
    string SourceTag,
    bool DryRun,
    bool ShowValues,
    int BatchSize);
