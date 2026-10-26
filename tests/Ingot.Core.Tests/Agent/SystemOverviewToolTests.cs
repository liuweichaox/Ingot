// 验证系统概览工具的全局范围、空授权和越权拒绝边界。

using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class SystemOverviewToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithNoAuthorizedSites_ReturnsScopedEmptyOverview()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Database=unused");
        var tool = new SystemOverviewTool(
            new EdgeRegistry(dataSource),
            new SystemSiteCatalog(dataSource),
            null!,
            null!,
            Options.Create(new ChatOptions()));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?>()
            },
            Context(new AgentAccessScope()));

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.Contains("没有可汇总的站点", result.Summary, StringComparison.Ordinal);
        Assert.Equal(0, result.Data.GetProperty("siteCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_WithUnauthorizedExplicitSite_RejectsBeforeReadingData()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Database=unused");
        var tool = new SystemOverviewTool(
            new EdgeRegistry(dataSource),
            new SystemSiteCatalog(dataSource),
            null!,
            null!,
            Options.Create(new ChatOptions()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-B" }
            },
            Context(new AgentAccessScope { SiteIds = new HashSet<string>(["SITE-A"]) })));
    }

    [Fact]
    public void Definition_DoesNotRequireSiteIdForGlobalQuestions()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Database=unused");
        var tool = new SystemOverviewTool(
            new EdgeRegistry(dataSource),
            new SystemSiteCatalog(dataSource),
            null!,
            null!,
            Options.Create(new ChatOptions()));
        var required = tool.Definition.InputSchema.GetProperty("required");

        Assert.Equal(JsonValueKind.Undefined, required.ValueKind);
    }

    private static AgentExecutionContext Context(AgentAccessScope scope)
        => new()
        {
            RunId = "run-system-overview-test",
            UserId = "operator",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Request = new CreateChatRunRequest { Question = "系统目前有几个采集站点？" },
            AccessScope = scope
        };
}
