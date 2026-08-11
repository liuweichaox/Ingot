using Ingot.Agent;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AnalysisToolTests
{
    private static readonly AgentExecutionContext ExecutionContext = new()
    {
        RunId = "run-test",
        UserId = "operator",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Request = new CreateChatRunRequest { Question = "test" }
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
        ]));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
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
            }));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
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
        var tool = new CheckDataQualityTool(new StubEventReader(rows));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Equal(1_202, result.Data.GetProperty("eventCount").GetInt32());
        Assert.Equal(0, result.Data.GetProperty("incompleteProcessExecutions").GetInt32());
        Assert.Equal(1, result.Data.GetProperty("unavailableProcessProcessExecutions").GetInt32());
        Assert.DoesNotContain(result.Limitations, limitation => limitation.Contains("500", StringComparison.Ordinal));
        Assert.Contains("已完整检查 1202 条", result.Summary, StringComparison.Ordinal);
    }

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
        Arguments = new Dictionary<string, string?> { ["executionId"] = executionId }
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
            CancellationToken ct = default)
            => Task.FromResult(rows);

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
