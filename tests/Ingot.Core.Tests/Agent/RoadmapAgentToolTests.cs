using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class RoadmapAgentToolTests
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
    public void RelatedRecordsVerifier_RejectsToolDataOver32Kb()
    {
        var verifier = new DefaultAnalysisResultValidator();
        var large = new string('x', 33 * 1024);
        var result = new AnalysisToolResult
        {
            Tool = "oversized",
            Summary = "oversized",
            Data = JsonSerializer.SerializeToElement(new { large }),
            RelatedRecords =
            [
                new RelatedRecordRef { Kind = "test", Id = "1", Label = "test" }
            ]
        };

        var ok = verifier.TryVerify([result], out _, out var error);

        Assert.False(ok);
        Assert.Contains("32768", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindComparableExecutions_ReturnsReasonsForMatches()
    {
        var rows = new[]
        {
            Row(1, "process.execution.started", "execution-a", new Dictionary<string, string>
            {
                ["product_code"] = "LENS-A",
                ["operation_code"] = "molding",
                ["process_specification_id"] = "R1",
                ["tooling_assembly_id"] = "MOLD-02"
            }),
            Row(2, "process.execution.completed", "execution-b", new Dictionary<string, string>
            {
                ["product_code"] = "LENS-A",
                ["operation_code"] = "molding",
                ["process_specification_id"] = "R1",
                ["tooling_assembly_id"] = "MOLD-01"
            }),
            Row(3, "process.execution.completed", "execution-c", new Dictionary<string, string>
            {
                ["product_code"] = "OTHER",
                ["operation_code"] = "molding",
                ["process_specification_id"] = "R2"
            })
        };
        var tool = new FindComparableExecutionsTool(new FilteringEventReader(rows));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["executionId"] = "execution-a" }
            },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        var comparable = result.Data.GetProperty("comparableProcessExecutions").EnumerateArray().Single();
        Assert.Equal("execution-b", comparable.GetProperty("executionId").GetString());
        Assert.Contains(
            comparable.GetProperty("matchedKeys").EnumerateArray(),
            key => key.GetString() == "product_code");
    }

    [Fact]
    public async Task CompareExecutions_ReportsRobustInspectionReferenceAndRelatedRecordsLinks()
    {
        var events = Enumerable.Range(1, 502)
            .Select(id => Row(
                id,
                id == 1 ? "process.execution.started" : id == 502 ? "process.execution.completed" : "process.sample",
                "execution-a"))
            .Concat(Enumerable.Range(1, 502)
                .Select(id => Row(
                    1_000 + id,
                    id == 1 ? "process.execution.started" : id == 502 ? "process.execution.completed" : "process.sample",
                    "execution-b")))
            .ToArray();
        var inspectionRows = Enumerable.Range(0, 501)
            .Select(id => Inspection("execution-a", "PASS", id == 500 ? 11m : id % 2 == 0 ? 10m : 12m))
            .Concat(Enumerable.Range(0, 501)
                .Select(id => Inspection("execution-b", id % 2 == 0 ? "PASS" : "FAIL", id == 500 ? 21m : id % 2 == 0 ? 20m : 22m)))
            .ToArray();
        var inspections = new StubInspectionStore(inspectionRows);
        var tool = new CompareExecutionsTool(new FilteringEventReader(events), inspections);

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?>
                {
                    ["baselineProcessExecutionId"] = "execution-a",
                    ["comparisonProcessExecutionIds"] = "execution-b"
                }
            },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.NotEmpty(result.Details);
        var characteristic = result.Data.GetProperty("inspection")
            .GetProperty("characteristics")
            .EnumerateArray()
            .Single();
        Assert.Equal("pv", characteristic.GetProperty("characteristicCode").GetString());
        Assert.Equal(11d, characteristic.GetProperty("baselineAverage").GetDouble());
        Assert.Equal(21d, characteristic.GetProperty("comparisonAverage").GetDouble());
        Assert.Equal(21d, characteristic.GetProperty("comparisonMedian").GetDouble());
        Assert.True(characteristic.GetProperty("robustDeviation").GetDouble() < 0);
        Assert.Equal(502, result.Data.GetProperty("eventSequence")
            .GetProperty("baselineProductionRecordCount").GetInt32());
        Assert.Equal(501, result.Data.GetProperty("inspection")
            .GetProperty("baselineInspectionCount").GetInt32());
        Assert.DoesNotContain(result.Limitations, item => item.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckDataQuality_DoesNotTreatPhaseMetadataAsProcessExecutionCompleteness()
    {
        var tool = new CheckDataQualityTool(new FilteringEventReader(
        [
            Row(1, "phase.anneal.started", "execution-a", new Dictionary<string, string>
            {
                ["phase_code"] = "unknown",
                ["phase_source"] = "estimated"
            })
        ]));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Contains(result.Limitations, item => item.Contains("过程数据", StringComparison.Ordinal));
        Assert.Equal(1, result.Data.GetProperty("unavailableProcessProcessExecutions").GetInt32());
    }

    private static PlatformProductionEvent Row(
        long ingestId,
        string eventType,
        string executionId,
        IReadOnlyDictionary<string, string>? context = null,
        DateTimeOffset? occurredAt = null)
    {
        var timestamp = occurredAt ?? DateTimeOffset.Parse("2026-07-18T10:00:00Z").AddSeconds(ingestId);
        return new PlatformProductionEvent
        {
            IngestId = ingestId,
            EdgeId = "EDGE-001",
            IngestedAt = timestamp,
            Event = new ProductionEvent
            {
                EventId = $"event-{ingestId}",
                EventType = eventType,
                OccurredAt = timestamp,
                RecordedAt = timestamp,
                Source = "test",
                Subject = new ObjectRef("asset", "PRESS-01"),
                Context = context ?? new Dictionary<string, string>
                {
                    ["product_code"] = "LENS-A",
                    ["operation_code"] = "molding",
                    ["process_specification_id"] = "R1"
                },
                ExecutionId = executionId,
                Seq = ingestId
            }
        };
    }

    private static InspectionRecord Inspection(string executionId, string outcome, decimal value)
        => new()
        {
            RecordId = Guid.CreateVersion7(),
            OutputItemId = $"wp-{executionId}-{value}",
            ExecutionId = executionId,
            DefinitionCode = "entryPoint",
            DefinitionVersion = 1,
            MeasuredAt = DateTimeOffset.Parse("2026-07-18T11:00:00Z"),
            RecordedAt = DateTimeOffset.Parse("2026-07-18T11:00:00Z"),
            IngestedAt = DateTimeOffset.Parse("2026-07-18T11:00:01Z"),
            Outcome = outcome,
            SubmittedBy = "operator",
            SubmitterVerified = true,
            Measurements =
            [
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = "pv",
                    Outcome = outcome,
                    NumericValue = value,
                    Unit = "um"
                }
            ]
        };

    private sealed class FilteringEventReader(IReadOnlyList<PlatformProductionEvent> rows) : IChatEventReader
    {
        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.ExecutionId))
                filtered = filtered.Where(row => row.Event.ExecutionId == query.ExecutionId);
            foreach (var pair in query.Context)
                filtered = filtered.Where(row =>
                    row.Event.Context.TryGetValue(pair.Key, out var value) &&
                    string.Equals(value, pair.Value, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(filtered.Take(query.Limit).ToArray());
        }

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.ExecutionId))
                filtered = filtered.Where(row => row.Event.ExecutionId == query.ExecutionId);
            foreach (var pair in query.Context)
                filtered = filtered.Where(row =>
                    row.Event.Context.TryGetValue(pair.Key, out var value) &&
                    string.Equals(value, pair.Value, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(filtered.ToArray());
        }

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            string userId,
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            var filtered = QueryAsync(userId, query with { Limit = 500 }, ct).Result;
            return Task.FromResult(new PlatformEventScopeStats
            {
                Count = filtered.Count,
                LatestOccurredAt = filtered.Count == 0 ? null : filtered.Max(static row => row.Event.OccurredAt),
                EarliestOccurredAt = filtered.Count == 0 ? null : filtered.Min(static row => row.Event.OccurredAt)
            });
        }
    }

    private sealed class StubInspectionStore(IReadOnlyList<InspectionRecord> records) : IInspectionRecordStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<StoreInspectionRecordResult> CreateAsync(
            CreateInspectionRecordRequest request,
            bool submitterVerified,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default)
            => Task.FromResult(records.FirstOrDefault(record => record.RecordId == recordId));

        public Task<IReadOnlyList<InspectionRecord>> QueryAsync(
            InspectionRecordQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(records
                .Where(record => string.IsNullOrWhiteSpace(query.ExecutionId) ||
                                 record.ExecutionId == query.ExecutionId)
                .Take(query.Limit)
                .ToArray());

        public Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default)
        {
            var ids = executionIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<InspectionRecord>>(
                records.Where(record => ids.Contains(record.ExecutionId)).ToArray());
        }
    }
}
