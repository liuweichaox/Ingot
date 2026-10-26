// 暴露 Ingot AI 助手的能力目录和只读边界，避免把能力说明混入业务数据查询。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class SystemCapabilitiesTool : IAnalysisTool
{
    private static readonly object[] Capabilities =
    [
        new
        {
            key = "system-overview",
            title = "系统概览",
            description = "汇总站点、现场节点、生产事件和质量检测记录的可见范围与数量。",
            requiredContext = "无需指定具体对象；也可以指定站点。",
            output = "返回站点级统计、节点状态和数据时间范围。"
        },
        new
        {
            key = "production-records",
            title = "生产运行记录",
            description = "查找生产执行、设备、批次和最近活动记录。",
            requiredContext = "通常需要站点、运行对象、执行编号或时间范围。",
            output = "返回可追溯的记录摘要和原始记录入口。"
        },
        new
        {
            key = "quality-check",
            title = "数据质量检查",
            description = "检查过程记录是否完整、是否存在缺失、断点或无法配对的数据。",
            requiredContext = "需要站点或具体生产执行范围。",
            output = "返回观察结果、缺口和仍需补充的范围。"
        },
        new
        {
            key = "process-analysis",
            title = "过程对比分析",
            description = "对比同类执行、时间窗口和过程参数差异。",
            requiredContext = "需要一个具体执行、产品或可比较的时间范围。",
            output = "返回差异、统计结果和相关记录引用。"
        },
        new
        {
            key = "process-knowledge",
            title = "工艺知识检索",
            description = "检索已接入且经过复核的工艺文档、作业指导和经验记录。",
            requiredContext = "需要明确的工艺问题、参数或文档关键词。",
            output = "返回匹配内容和知识来源。"
        }
    ];

    private static readonly string[] Boundaries =
    [
        "回答基于当前账号可见的系统数据。",
        "没有数据、数据不完整或缺少查询范围时，会明确说明限制。",
        "系统概览是站点级统计；需要明细时可以继续指定站点、执行、设备或时间范围。"
    ];

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_system_capabilities",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "说明 Ingot AI 助手当前可查询的数据域和能力，不读取业务明细。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false
        })
    };

    public Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var scope = context.AccessScope.AllowAllSites
            ? "当前账号的全部授权范围"
            : context.AccessScope.SiteIds.Count == 0
                ? "当前账号没有已授权站点"
                : $"当前账号已授权的 {context.AccessScope.SiteIds.Count} 个站点";

        return Task.FromResult(new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"Ingot AI 助手支持生产、质量、过程和工艺知识查询，数据范围受权限控制（{scope}）。",
            Data = JsonSerializer.SerializeToElement(new
            {
                assistant = "Ingot AI 助手",
                scope,
                capabilities = Capabilities,
                boundaries = Boundaries
            }),
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "system-capability",
                    Id = Definition.Name,
                    Label = "Ingot AI 助手能力目录",
                    Url = "/chat"
                }
            ],
            Limitations = ["具体可回答范围还取决于当前账号权限和系统中已接入的数据。"],
            Provenance = new AgentProvenance
            {
                ReadAt = DateTimeOffset.UtcNow
            },
            Outcome = AnalysisToolOutcomes.Sufficient
        });
    }
}
