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
            Row(1, 1, "cycle.started", later, "cycle-1"),
            Row(2, 2, "cycle.completed", earlier, "cycle-1")
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
    public async Task CheckDataQuality_RejectsAWindowAtTheQueryLimit()
    {
        var rows = Enumerable.Range(1, 500)
            .Select(index => Row(
                index,
                index,
                "telemetry.observed",
                DateTimeOffset.Parse("2026-07-18T10:00:00Z").AddSeconds(index)))
            .ToArray();
        var tool = new CheckDataQualityTool(new StubEventReader(rows));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Contains(result.Limitations, limitation => limitation.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCycleTrace_UsesTheFirstStartedEventAsCycleStart()
    {
        var observed = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var started = observed.AddSeconds(1);
        var completed = observed.AddSeconds(4);
        var tool = new GetCycleTraceTool(new StubEventReader(
        [
            Row(1, 1, "alarm.observed", observed, "cycle-1"),
            Row(2, 2, "cycle.started", started, "cycle-1"),
            Row(3, 3, "cycle.completed", completed, "cycle-1")
        ]));

        var result = await tool.ExecuteAsync(CycleCall(tool, "cycle-1"), ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.Equal(started, result.Data.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(3_000d, result.Data.GetProperty("durationMs").GetDouble());
    }

    [Fact]
    public async Task GetCycleTrace_RejectsMissingStartAndTruncatedTimeline()
    {
        var completedOnly = new GetCycleTraceTool(new StubEventReader(
        [
            Row(1, 1, "cycle.completed", DateTimeOffset.Parse("2026-07-18T10:00:00Z"), "cycle-1")
        ]));

        var missingStart = await completedOnly.ExecuteAsync(
            CycleCall(completedOnly, "cycle-1"),
            ExecutionContext);
        Assert.Equal(AnalysisToolOutcomes.InsufficientData, missingStart.Outcome);
        Assert.Contains(missingStart.Limitations,
            limitation => limitation.Contains("加工开始记录", StringComparison.Ordinal));

        var start = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = Enumerable.Range(1, 500)
            .Select(index => Row(
                index,
                index,
                index == 1 ? "cycle.started" : index == 500 ? "cycle.completed" : "cycle.observed",
                start.AddSeconds(index),
                "cycle-2"))
            .ToArray();
        var truncatedTool = new GetCycleTraceTool(new StubEventReader(rows));

        var truncated = await truncatedTool.ExecuteAsync(
            CycleCall(truncatedTool, "cycle-2"),
            ExecutionContext);
        Assert.Equal(AnalysisToolOutcomes.InsufficientData, truncated.Outcome);
        Assert.Contains(truncated.Limitations,
            limitation => limitation.Contains("500", StringComparison.Ordinal));
    }

    private static AnalysisToolCall CycleCall(GetCycleTraceTool tool, string correlationId) => new()
    {
        Tool = tool.Definition.Name,
        Arguments = new Dictionary<string, string?> { ["correlationId"] = correlationId }
    };

    private static PlatformProductionEvent Row(
        long ingestId,
        long sequence,
        string eventType,
        DateTimeOffset occurredAt,
        string? correlationId = null)
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
                CorrelationId = correlationId,
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
