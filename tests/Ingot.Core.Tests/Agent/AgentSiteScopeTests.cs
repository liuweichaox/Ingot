// 验证 Agent 工具在跨站请求、隐式范围和受限数据读取时安全失败。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.AgentTools;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AgentSiteScopeTests
{
    [Fact]
    public async Task CompareTimeWindows_RequiresAuthorizedSiteAndPassesItToService()
    {
        var service = new RecordingTimeWindowComparisonService();
        var tool = new CompareTimeWindowsTool(service);

        await tool.ExecuteAsync(Call("SITE-001"), Context("SITE-001"));

        Assert.Equal("SITE-001", service.SiteId);
        Assert.Contains("siteId", tool.Definition.InputSchema.GetProperty("required")
            .EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public async Task CompareTimeWindows_RejectsUnauthorizedSiteBeforeCallingService()
    {
        var service = new RecordingTimeWindowComparisonService();
        var tool = new CompareTimeWindowsTool(service);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ExecuteAsync(Call("SITE-OTHER"), Context("SITE-001")));

        Assert.Null(service.SiteId);
    }

    [Fact]
    public async Task ListDataObjects_UsesTokenSiteScopeEvenWhenLegacyReaderScopeIsBroader()
    {
        var reader = new RecordingDataObjectReader();
        var tool = new ListDataObjectsTool(reader);
        var call = new AnalysisToolCall
        {
            Tool = tool.Definition.Name,
            Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-001" }
        };

        await tool.ExecuteAsync(call, Context("SITE-001"));

        Assert.Equal("SITE-001", reader.SiteId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ExecuteAsync(call with
            {
                Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-OTHER" }
            }, Context("SITE-001")));
        Assert.Equal(1, reader.CallCount);
    }

    private static AnalysisToolCall Call(string siteId) => new()
    {
        Tool = "compare_time_windows",
        Arguments = new Dictionary<string, string?>
        {
            ["siteId"] = siteId,
            ["baselineWindowId"] = "baseline",
            ["windowsJson"] = JsonSerializer.Serialize(new[]
            {
                new TimeWindowSelection
                {
                    WindowId = "baseline",
                    SubjectType = "equipment",
                    SubjectId = "PRESS-01",
                    From = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                    To = DateTimeOffset.Parse("2026-08-01T01:00:00Z")
                }
            })
        }
    };

    private static AgentExecutionContext Context(string siteId) => new()
    {
        RunId = "run-site",
        UserId = "operator",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Request = new CreateChatRunRequest { Question = "比较窗口" },
        AccessScope = new AgentAccessScope
        {
            SiteIds = new HashSet<string>([siteId], StringComparer.OrdinalIgnoreCase)
        }
    };

    private sealed class RecordingTimeWindowComparisonService : ITimeWindowComparisonService
    {
        public string? SiteId { get; private set; }

        public Task<TimeWindowComparisonResult> CompareAsync(
            TimeWindowComparisonRequest request,
            string siteId,
            CancellationToken ct = default)
        {
            SiteId = siteId;
            var row = new TimeWindowComparisonRow
            {
                WindowId = "baseline",
                SubjectType = "equipment",
                SubjectId = "PRESS-01",
                From = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                To = DateTimeOffset.Parse("2026-08-01T01:00:00Z"),
                Quality = new TimeWindowQualitySummary { InspectionCount = 1 }
            };
            return Task.FromResult(new TimeWindowComparisonResult
            {
                BaselineWindowId = "baseline",
                AnalysisPlanId = "plan",
                DataModelId = "model",
                AnalysisScope = "analysis-window",
                AlignmentMode = "wall-clock",
                Baseline = row
            });
        }
    }

    private sealed class RecordingDataObjectReader : IChatDataObjectReader
    {
        public string? SiteId { get; private set; }
        public int CallCount { get; private set; }

        public Task<DataObjectPage> QueryDataObjectsAsync(
            string userId,
            DataObjectQuery query,
            CancellationToken ct = default)
        {
            CallCount++;
            SiteId = query.SiteId;
            return Task.FromResult(new DataObjectPage { Limit = query.Limit, Offset = query.Offset });
        }
    }
}
