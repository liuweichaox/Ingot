// 验证 Agent 的 ListDataObjectsTool 能力、只读边界和拒绝路径。

using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Analytics;
using Ingot.Platform.Infrastructure.AgentTools;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class ListDataObjectsToolTests
{
    [Fact]
    public async Task DeterministicPlanner_RoutesRunningObjectQuestionToSummaryTool()
    {
        var tool = new ListDataObjectsTool(new StubDataObjectReader([]));
        var result = await new DeterministicModelClient().ResolveIntentAsync(
            new CreateChatRunRequest
            {
                Question = "当前有哪些运行对象，各自最近一次活动时间是什么？"
            },
            [tool.Definition]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("list_data_objects", call.Tool);
        var validator = new DefaultPlanValidator(Options.Create(new ChatOptions { MaxToolCalls = 8 }));
        Assert.True(
            validator.TryValidate(
                ProductEntryPoints.Chat,
                result.Value with { EntryPoint = ProductEntryPoints.Chat },
                new Dictionary<string, IAnalysisTool> { [tool.Definition.Name] = tool },
                out var error),
            error);
    }

    [Fact]
    public async Task Tool_ReturnsRunningObjectsWithoutScanningEventDetails()
    {
        var observedAt = DateTimeOffset.Parse("2026-07-24T19:03:26Z");
        var tool = new ListDataObjectsTool(new StubDataObjectReader(
        [
            new DataObjectSummary
            {
                SiteId = "SITE-001",
                SubjectType = "equipment",
                SubjectId = "FURNACE-001",
                EdgeId = "EDGE-001",
                EventCount = 200,
                SampleCount = 180,
                LastObservedAt = observedAt
            }
        ]));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
            new AgentExecutionContext
            {
                RunId = "run-1",
                UserId = "operator",
                EntryPoint = ProductEntryPoints.Chat,
                Purpose = RunPurposes.ReadOnlyAnalysis,
                Request = new CreateChatRunRequest { Question = "当前有哪些运行对象？" }
            });

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.Contains("FURNACE-001", result.Summary, StringComparison.Ordinal);
        Assert.Contains("180 个样本", result.Summary, StringComparison.Ordinal);
        Assert.Equal(1, result.Data.GetProperty("total").GetInt32());
        Assert.Equal("/explorer", Assert.Single(result.RelatedRecords).Url);
    }

    private sealed class StubDataObjectReader(IReadOnlyList<DataObjectSummary> rows) : IChatDataObjectReader
    {
        public Task<DataObjectPage> QueryDataObjectsAsync(
            string userId,
            DataObjectQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new DataObjectPage
            {
                Data = rows,
                Total = rows.Count,
                Limit = query.Limit,
                Offset = query.Offset
            });
    }
}
