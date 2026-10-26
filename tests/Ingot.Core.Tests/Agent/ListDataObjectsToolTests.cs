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
    [Theory]
    [InlineData("系统目前有几个采集站点？")]
    [InlineData("当前系统有没有质量检测数据？")]
    [InlineData("整个系统最近有没有收到生产数据？")]
    public async Task DeterministicPlanner_RoutesGlobalQuestionToSystemOverview(string question)
    {
        var result = await new DeterministicModelClient().ResolveIntentAsync(
            new CreateChatRunRequest
            {
                Question = question,
                PageContext = new PageContextRef { Kind = "execution", Id = "EXEC-001" }
            },
            [SystemOverviewDefinition()]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("get_system_overview", call.Tool);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public async Task DeterministicPlanner_RoutesCapabilityQuestionToCapabilityCatalog()
    {
        var result = await new DeterministicModelClient().ResolveIntentAsync(
            new CreateChatRunRequest { Question = "这个系统能做什么？" },
            [SystemCapabilitiesDefinition(), SystemOverviewDefinition()]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("get_system_capabilities", call.Tool);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public async Task DeterministicPlanner_BindsCurrentSiteForScopedSystemQuestion()
    {
        var result = await new DeterministicModelClient().ResolveIntentAsync(
            new CreateChatRunRequest
            {
                Question = "这个站点在线吗？",
                PageContext = new PageContextRef
                {
                    Kind = "execution",
                    Id = "EXEC-001",
                    SiteId = "SITE-001"
                }
            },
            [SystemOverviewDefinition()]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("get_system_overview", call.Tool);
        Assert.Equal("SITE-001", call.Arguments["siteId"]);
    }

    [Fact]
    public async Task DeterministicPlanner_BindsExecutionPageContextSite()
    {
        var tool = new AnalysisToolDefinition
        {
            Name = "get_execution_trace",
            Version = "1.0.0",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "读取执行轨迹。",
            InputSchema = System.Text.Json.JsonDocument.Parse(
                "{\"type\":\"object\",\"properties\":{\"siteId\":{\"type\":\"string\"},\"executionId\":{\"type\":\"string\"}}}").RootElement
        };

        var result = await new DeterministicModelClient().ResolveIntentAsync(
            new CreateChatRunRequest
            {
                Question = "查看这次执行的完整过程。",
                PageContext = new PageContextRef
                {
                    Kind = "execution",
                    Id = "EXEC-001",
                    SiteId = "SITE-001"
                }
            },
            [tool]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("SITE-001", call.Arguments["siteId"]);
        Assert.Equal("EXEC-001", call.Arguments["executionId"]);
    }

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
        call = call with
        {
            Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-001" }
        };
        var validator = new DefaultPlanValidator(Options.Create(new ChatOptions { MaxToolCalls = 8 }));
        Assert.True(
            validator.TryValidate(
                ProductEntryPoints.Chat,
                result.Value with
                {
                    EntryPoint = ProductEntryPoints.Chat,
                    ToolCalls = [call]
                },
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
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-001" }
            },
            new AgentExecutionContext
            {
                RunId = "run-1",
                UserId = "operator",
                EntryPoint = ProductEntryPoints.Chat,
                Purpose = RunPurposes.ReadOnlyAnalysis,
                Request = new CreateChatRunRequest { Question = "当前有哪些运行对象？" },
                AccessScope = new AgentAccessScope { AllowAllSites = true }
            });

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.Contains("FURNACE-001", result.Summary, StringComparison.Ordinal);
        Assert.Contains("180 个样本", result.Summary, StringComparison.Ordinal);
        Assert.Equal(1, result.Data.GetProperty("total").GetInt32());
        Assert.Equal("/explorer?siteId=SITE-001", Assert.Single(result.RelatedRecords).Url);
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

    private static AnalysisToolDefinition SystemOverviewDefinition()
        => new()
        {
            Name = "get_system_overview",
            Version = "1.0.0",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "查询授权范围内的系统概览。",
            InputSchema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}").RootElement
        };

    private static AnalysisToolDefinition SystemCapabilitiesDefinition()
        => new()
        {
            Name = "get_system_capabilities",
            Version = "1.0.0",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "说明系统能力。",
            InputSchema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}").RootElement
        };
}
