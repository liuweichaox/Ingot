using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Cycles;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class CompareProcessWindowsTool(IProcessWindowComparisonService comparisons) : IAnalysisTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "compare_process_windows",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "比较连续运行设备的两个或多个时间窗口，并联合返回工艺信号和对应质量结果。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "baselineWindowId", "windowsJson" },
            properties = new
            {
                baselineWindowId = new { type = "string", minLength = 1, maxLength = 200 },
                windowsJson = new
                {
                    type = "string",
                    description = "JSON 数组；每项包含 windowId、subjectType、subjectId、from、to，可选 label。"
                }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var baselineWindowId = Require(call, "baselineWindowId");
        var windows = JsonSerializer.Deserialize<ProcessAnalysisWindowSelection[]>(
            Require(call, "windowsJson"),
            JsonOptions) ?? [];
        var result = await comparisons.CompareAsync(new ProcessWindowComparisonRequest
        {
            AnalysisScope = "analysis-window",
            BaselineWindowId = baselineWindowId,
            Windows = windows
        }, ct).ConfigureAwait(false);
        var rows = new[] { result.Baseline }.Concat(result.ComparisonWindows).ToArray();
        var qualityLinked = rows.Count(static row => row.Quality.InspectionCount > 0);
        var limitations = new List<string>();
        if (qualityLinked < rows.Length)
            limitations.Add($"{rows.Length - qualityLinked} 个窗口没有关联质量检测结果，不能据此判断工艺好坏。");

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"已比较 {rows.Length} 个连续过程窗口，{qualityLinked} 个窗口关联了质量检测结果。",
            Data = JsonSerializer.SerializeToElement(result, JsonOptions),
            RelatedRecords = rows.Select(row => new RelatedRecordRef
            {
                Kind = "event-query",
                Id = $"window:{row.WindowId}",
                Label = row.Label ?? row.WindowId,
                Url = $"/events?subjectId={Uri.EscapeDataString(row.SubjectId)}&from={Uri.EscapeDataString(row.From.ToString("O"))}&to={Uri.EscapeDataString(row.To.ToString("O"))}"
            }).ToArray(),
            Limitations = limitations,
            Outcome = qualityLinked > 0 ? AnalysisToolOutcomes.Sufficient : AnalysisToolOutcomes.InsufficientData
        };
    }

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));
}
