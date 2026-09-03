// 读取同时满足项目成员关系与站点授权的研发项目证据。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class GetResearchProjectTool(
    ProcessResearchWorkflow workflow,
    IProcessResearchStore store) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_research_project",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description =
            "读取工艺研发项目的目标、变量、假设、真实运行证据、工艺窗口和知识声明，用于组织下一步研发分析。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "projectId" },
            properties = new
            {
                projectId = new
                {
                    type = "string",
                    format = "uuid",
                    description = "工艺研发项目标识"
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
        if (!call.Arguments.TryGetValue("projectId", out var projectIdText) ||
            !Guid.TryParse(projectIdText, out var projectId))
            throw new ArgumentException("请提供有效的工艺研发项目标识。", nameof(call));

        try
        {
            var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false);
            var userId = context.UserId.Trim().ToLowerInvariant();
            if (project is null ||
                !(string.Equals(project.OwnerUserId, userId, StringComparison.Ordinal) ||
                  project.MemberUserIds.Contains(userId, StringComparer.Ordinal)))
                throw new ProcessResearchRuleException("研发项目不存在或当前用户无权访问。");
            context.AccessScope.EnsureAuthorizedSite(project.SiteCode);
            var workspace = await workflow.GetWorkspaceAsync(projectId, ct).ConfigureAwait(false);
            var validatedWindows = workspace.OperatingRegions.Count(
                static value => value.Status == "validated");
            return new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary =
                    $"研发项目“{workspace.Project.Name}”包含 {workspace.Hypotheses.Count} 条假设、" +
                    $"{workspace.RecipeRecommendationFlows.Count} 条配方建议闭环和 {validatedWindows} 个已验证工艺窗口。",
                Data = JsonSerializer.SerializeToElement(workspace),
                RelatedRecords =
                [
                    new RelatedRecordRef
                    {
                        Kind = "research-project",
                        Id = workspace.Project.ProjectId.ToString(),
                        Label = workspace.Project.Name
                    }
                ],
                Outcome = AnalysisToolOutcomes.Sufficient
            };
        }
        catch (ProcessResearchRuleException)
        {
            return new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary = "没有找到指定的工艺研发项目。",
                Data = JsonSerializer.SerializeToElement(new { projectId }),
                Limitations = ["项目标识无效或项目当前不可访问。"],
                Outcome = AnalysisToolOutcomes.InsufficientData
            };
        }
    }
}
