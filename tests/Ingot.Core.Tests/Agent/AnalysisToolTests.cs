// 验证 Agent 的 AnalysisTool 能力、只读边界和拒绝路径。

using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AnalysisToolTests
{
    private static readonly AgentAccessScope SiteScope = new()
    {
        SiteIds = new HashSet<string>(["SITE-001"], StringComparer.OrdinalIgnoreCase)
    };
    private static readonly AgentExecutionContext ExecutionContext = new()
    {
        RunId = "run-test",
        UserId = "operator",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Request = new CreateChatRunRequest { Question = "test" },
        AccessScope = SiteScope
    };

    [Fact]
    public async Task CheckDataQuality_UsesLatestOccurredAtInsteadOfIngestOrder()
    {
        var later = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var earlier = later.AddMinutes(-10);
        var tool = new CheckDataQualityTool(new StubEventReader(
        [
            Row(1, 1, "process.execution.started", later, "execution-1"),
            Row(2, 2, "process.execution.completed", earlier, "execution-1")
        ]), EmptyTimeSeriesStore.Instance);

        var result = await tool.ExecuteAsync(
            QualityCall(tool),
            ExecutionContext);

        Assert.Equal(later, result.Data.GetProperty("latestOccurredAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task CheckDataQuality_ReportsFullScopeFreshnessAndCountBeyondDetailWindow()
    {
        var windowLatest = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var trueLatest = windowLatest.AddHours(6);
        var tool = new CheckDataQualityTool(new StubEventReader(
            [Row(1, 1, "telemetry.observed", windowLatest)],
            new PlatformEventScopeStats
            {
                Count = 4200,
                LatestOccurredAt = trueLatest,
                EarliestOccurredAt = windowLatest
            }), EmptyTimeSeriesStore.Instance);

        var result = await tool.ExecuteAsync(
            QualityCall(tool),
            ExecutionContext);

        Assert.Equal(trueLatest, result.Data.GetProperty("latestOccurredAt").GetDateTimeOffset());
        Assert.Equal(4200, result.Data.GetProperty("totalEventCount").GetInt64());
    }

    [Fact]
    public async Task CheckDataQuality_InspectsEveryRowBeyondTransportPageSize()
    {
        var start = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = Enumerable.Range(1, 1_202)
            .Select(index => Row(
                index,
                index,
                index == 1 ? "process.execution.started" : index == 1_202 ? "process.execution.completed" : "telemetry.observed",
                start.AddSeconds(index),
                "execution-large"))
            .ToArray();
        var tool = new CheckDataQualityTool(new StubEventReader(rows), EmptyTimeSeriesStore.Instance);

        var result = await tool.ExecuteAsync(
            QualityCall(tool),
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Equal(1_202, result.Data.GetProperty("eventCount").GetInt32());
        Assert.Equal(0, result.Data.GetProperty("incompleteProcessExecutions").GetInt32());
        Assert.Equal(1, result.Data.GetProperty("unavailableProcessProcessExecutions").GetInt32());
        Assert.DoesNotContain(result.Limitations, limitation => limitation.Contains("500", StringComparison.Ordinal));
        Assert.Contains("已完整检查 1202 条", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckDataQuality_RejectsUnauthorizedSiteBeforeReadingData()
    {
        var tool = new CheckDataQualityTool(new StubEventReader([]), EmptyTimeSeriesStore.Instance);
        var call = QualityCall(tool) with
        {
            Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-OTHER" }
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ExecuteAsync(call, ExecutionContext));
    }

    [Fact]
    public async Task CheckDataQuality_FailsBeforeScanningExecutionsBeyondBudget()
    {
        var at = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var tool = new CheckDataQualityTool(
            new StubEventReader(
            [
                Row(1, 1, "process.execution.started", at, "execution-1"),
                Row(2, 2, "process.execution.started", at, "execution-2")
            ]),
            EmptyTimeSeriesStore.Instance,
            options: Options.Create(new ChatOptions { MaxProcessExecutionsPerTool = 1 }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.ExecuteAsync(QualityCall(tool), ExecutionContext));

        Assert.Contains("超过 1 个预算", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckDataQuality_PassesExplicitEventRowBudgetToReader()
    {
        var at = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var tool = new CheckDataQualityTool(
            new StubEventReader(
            [
                Row(1, 1, "telemetry.observed", at),
                Row(2, 2, "telemetry.observed", at.AddSeconds(1))
            ]),
            EmptyTimeSeriesStore.Instance,
            options: Options.Create(new ChatOptions { MaxEventRowsPerTool = 1 }));

        var error = await Assert.ThrowsAsync<ChatDataQueryLimitExceededException>(() =>
            tool.ExecuteAsync(QualityCall(tool), ExecutionContext));

        Assert.Equal(1, error.MaximumRows);
    }

    private static AnalysisToolCall QualityCall(IAnalysisTool tool) => new()
    {
        Tool = tool.Definition.Name,
        Arguments = new Dictionary<string, string?> { ["siteId"] = "SITE-001" }
    };

    [Fact]
    public async Task GetProcessExecutionTrace_UsesTheFirstStartedEventAsProcessExecutionStart()
    {
        var observed = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var started = observed.AddSeconds(1);
        var completed = observed.AddSeconds(4);
        var tool = new GetProcessExecutionTraceTool(new StubEventReader(
        [
            Row(1, 1, "alarm.observed", observed, "execution-1"),
            Row(2, 2, "process.execution.started", started, "execution-1"),
            Row(3, 3, "process.execution.completed", completed, "execution-1")
        ]));

        var result = await tool.ExecuteAsync(ProcessExecutionCall(tool, "execution-1"), ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.Equal(started, result.Data.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(3_000d, result.Data.GetProperty("durationMs").GetDouble());
    }

    [Fact]
    public async Task GetProcessExecutionTrace_RejectsMissingStartAndReadsCompleteLargeTimeline()
    {
        var completedOnly = new GetProcessExecutionTraceTool(new StubEventReader(
        [
            Row(1, 1, "process.execution.completed", DateTimeOffset.Parse("2026-07-18T10:00:00Z"), "execution-1")
        ]));

        var missingStart = await completedOnly.ExecuteAsync(
            ProcessExecutionCall(completedOnly, "execution-1"),
            ExecutionContext);
        Assert.Equal(AnalysisToolOutcomes.InsufficientData, missingStart.Outcome);
        Assert.Contains(missingStart.Limitations,
            limitation => limitation.Contains("加工开始记录", StringComparison.Ordinal));

        var start = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = Enumerable.Range(1, 602)
            .Select(index => Row(
                index,
                index,
                index == 1 ? "process.execution.started" : index == 602 ? "process.execution.completed" : "process.sample",
                start.AddSeconds(index),
                "execution-2"))
            .ToArray();
        var completeTool = new GetProcessExecutionTraceTool(new StubEventReader(rows));

        var complete = await completeTool.ExecuteAsync(
            ProcessExecutionCall(completeTool, "execution-2"),
            ExecutionContext);
        Assert.Equal(AnalysisToolOutcomes.Sufficient, complete.Outcome);
        Assert.Equal(602, complete.Data.GetProperty("eventCount").GetInt32());
        Assert.Contains(
            complete.Data.GetProperty("eventTypes").EnumerateArray(),
            item => item.GetProperty("eventType").GetString() == "process.sample" &&
                    item.GetProperty("count").GetInt32() == 600);
        Assert.DoesNotContain(complete.Limitations,
            limitation => limitation.Contains("500", StringComparison.Ordinal));
        Assert.True(new DefaultAnalysisResultValidator().TryVerify(
            [complete],
            out _,
            out var validationError),
            validationError);
    }

    private static AnalysisToolCall ProcessExecutionCall(GetProcessExecutionTraceTool tool, string executionId) => new()
    {
        Tool = tool.Definition.Name,
        Arguments = new Dictionary<string, string?>
        {
            ["siteId"] = "SITE-001",
            ["executionId"] = executionId
        }
    };

    private static PlatformProductionEvent Row(
        long ingestId,
        long sequence,
        string eventType,
        DateTimeOffset occurredAt,
        string? executionId = null)
        => new()
        {
            IngestId = ingestId,
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            IngestedAt = occurredAt.AddSeconds(1),
            Event = new ProductionEvent
            {
                EventId = $"event-{ingestId}",
                EventType = eventType,
                OccurredAt = occurredAt,
                RecordedAt = occurredAt,
                Source = "test",
                Subject = new ObjectRef("asset", "ASSET-001"),
                Context = new Dictionary<string, string> { ["operation"] = "test" },
                ExecutionId = executionId,
                Seq = sequence
            }
        };

    private sealed class StubEventReader(
        IReadOnlyList<PlatformProductionEvent> rows,
        PlatformEventScopeStats? stats = null) : IChatEventReader
    {
        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult(rows);

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default,
            int? maximumRows = null)
            => maximumRows.HasValue && rows.Count > maximumRows.Value
                ? Task.FromException<IReadOnlyList<PlatformProductionEvent>>(
                    new ChatDataQueryLimitExceededException(maximumRows.Value))
                : Task.FromResult(rows);

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult(stats ?? new PlatformEventScopeStats
            {
                Count = rows.Count,
                LatestOccurredAt = rows.Count == 0 ? null : rows.Max(static row => row.Event.OccurredAt),
                EarliestOccurredAt = rows.Count == 0 ? null : rows.Min(static row => row.Event.OccurredAt)
            });
    }
}
