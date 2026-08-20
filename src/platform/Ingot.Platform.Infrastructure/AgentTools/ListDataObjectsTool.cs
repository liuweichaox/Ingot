// 实现只读 Agent 工具 ListDataObjectsTool，仅暴露授权范围内的确定性证据。

using System.Globalization;
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class ListDataObjectsTool(IChatDataObjectReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "list_data_objects",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "查询已经上报生产数据的运行对象、设备及其最近活动时间和数据量。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                subjectType = new { type = "string" },
                subjectId = new { type = "string" },
                limit = new { type = "string", minLength = 1, maxLength = 3 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        call.Arguments.TryGetValue("subjectType", out var subjectType);
        call.Arguments.TryGetValue("subjectId", out var subjectId);
        call.Arguments.TryGetValue("limit", out var limitValue);
        var limit = int.TryParse(limitValue, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 100)
            : 100;
        var page = await events.QueryDataObjectsAsync(
            context.UserId,
            new DataObjectQuery
            {
                SubjectType = NullIfBlank(subjectType)?.ToLowerInvariant(),
                SubjectId = NullIfBlank(subjectId),
                Limit = limit
            },
            ct).ConfigureAwait(false);
        var objects = page.Data;
        var summary = objects.Count == 0
            ? "当前没有符合条件的运行对象。"
            : $"当前共 {page.Total} 个运行对象：" + string.Join(
                "；",
                objects.Select(static item =>
                    $"{item.SubjectId}（{ObjectTypeLabel(item.SubjectType)}），最近活动时间 {FormatTime(item.LastObservedAt)}，" +
                    $"{item.SampleCount:N0} 个样本"));
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                total = page.Total,
                objects
            }),
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "data-object-query",
                    Id = $"data-objects:{subjectType ?? "*"}:{subjectId ?? "*"}",
                    Label = $"运行对象查询结果（{page.Total} 个对象）",
                    Url = "/explorer"
                }
            ],
            Limitations = objects.Count == 0 ? ["当前尚未收到符合条件的生产数据。"] : [],
            Outcome = objects.Count == 0
                ? AnalysisToolOutcomes.InsufficientData
                : AnalysisToolOutcomes.Sufficient
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatTime(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "暂无";

    private static string ObjectTypeLabel(string value)
        => value switch
        {
            "equipment" => "设备",
            _ => value
        };
}
