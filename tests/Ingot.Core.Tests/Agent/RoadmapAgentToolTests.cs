using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Infrastructure.AgentTools;
using Ingot.Central.Infrastructure.Events;
using Ingot.Central.Infrastructure.Inspections;
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
        ActorId = "operator",
        Surface = ProductSurfaces.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Request = new CreateChatRunRequest { Question = "test" }
    };

    [Fact]
    public void EvidenceVerifier_RejectsToolDataOver32Kb()
    {
        var verifier = new DefaultEvidenceVerifier();
        var large = new string('x', 33 * 1024);
        var result = new AnalysisToolResult
        {
            Tool = "oversized",
            Summary = "oversized",
            Data = JsonSerializer.SerializeToElement(new { large }),
            Evidence =
            [
                new EvidenceRef { Kind = "test", Id = "1", Label = "test" }
            ]
        };

        var ok = verifier.TryVerify([result], out _, out var error);

        Assert.False(ok);
        Assert.Contains("32768", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindComparableCycles_ReturnsReasonsForMatches()
    {
        var rows = new[]
        {
            Row(1, "cycle.started", "cycle-a", new Dictionary<string, string>
            {
                ["product_code"] = "LENS-A",
                ["operation_code"] = "molding",
                ["recipe_id"] = "R1",
                ["mold_id"] = "MOLD-02"
            }),
            Row(2, "cycle.completed", "cycle-b", new Dictionary<string, string>
            {
                ["product_code"] = "LENS-A",
                ["operation_code"] = "molding",
                ["recipe_id"] = "R1",
                ["mold_id"] = "MOLD-01"
            }),
            Row(3, "cycle.completed", "cycle-c", new Dictionary<string, string>
            {
                ["product_code"] = "OTHER",
                ["operation_code"] = "molding",
                ["recipe_id"] = "R2"
            })
        };
        var tool = new FindComparableCyclesTool(new FilteringEventReader(rows));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["correlationId"] = "cycle-a" }
            },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        var comparable = result.Data.GetProperty("comparableCycles").EnumerateArray().Single();
        Assert.Equal("cycle-b", comparable.GetProperty("correlationId").GetString());
        Assert.Contains(
            comparable.GetProperty("matchedKeys").EnumerateArray(),
            key => key.GetString() == "product_code");
    }

    [Fact]
    public async Task CompareCycles_ComputesInspectionEffectSizeAndEvidenceLinks()
    {
        var events = new[]
        {
            Row(1, "cycle.started", "cycle-a"),
            Row(2, "cycle.completed", "cycle-a", occurredAt: DateTimeOffset.Parse("2026-07-18T10:01:00Z")),
            Row(3, "cycle.started", "cycle-b"),
            Row(4, "cycle.completed", "cycle-b", occurredAt: DateTimeOffset.Parse("2026-07-18T10:02:00Z"))
        };
        var inspections = new StubInspectionStore(
        [
            Inspection("cycle-a", "PASS", 10m),
            Inspection("cycle-a", "PASS", 12m),
            Inspection("cycle-b", "FAIL", 20m),
            Inspection("cycle-b", "PASS", 22m)
        ]);
        var tool = new CompareCyclesTool(new FilteringEventReader(events), inspections);

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?>
                {
                    ["baselineCorrelationId"] = "cycle-a",
                    ["candidateCorrelationIds"] = "cycle-b"
                }
            },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        Assert.NotEmpty(result.Artifacts);
        var characteristic = result.Data.GetProperty("inspection")
            .GetProperty("characteristics")
            .EnumerateArray()
            .Single();
        Assert.Equal("pv", characteristic.GetProperty("characteristicCode").GetString());
        Assert.Equal(11d, characteristic.GetProperty("baselineMean").GetDouble());
        Assert.Equal(21d, characteristic.GetProperty("candidateMean").GetDouble());
        Assert.True(characteristic.GetProperty("effectSize").GetDouble() > 0);
    }

    [Fact]
    public async Task CheckDataQuality_FlagsPhasePairingAndInferredAttribution()
    {
        var tool = new CheckDataQualityTool(new FilteringEventReader(
        [
            Row(1, "phase.anneal.started", "cycle-a", new Dictionary<string, string>
            {
                ["phase_code"] = "unknown",
                ["provenance"] = "inferred"
            })
        ]));

        var result = await tool.ExecuteAsync(
            new AnalysisToolCall { Tool = tool.Definition.Name },
            ExecutionContext);

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Contains(result.Limitations, item => item.Contains("阶段", StringComparison.Ordinal));
        Assert.Equal(1, result.Data.GetProperty("inferredPhaseAttribution").GetInt32());
    }

    private static CentralProductionEvent Row(
        long ingestId,
        string eventType,
        string correlationId,
        IReadOnlyDictionary<string, string>? context = null,
        DateTimeOffset? occurredAt = null)
    {
        var timestamp = occurredAt ?? DateTimeOffset.Parse("2026-07-18T10:00:00Z").AddSeconds(ingestId);
        return new CentralProductionEvent
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
                    ["recipe_id"] = "R1"
                },
                CorrelationId = correlationId,
                Seq = ingestId
            }
        };
    }

    private static InspectionRecord Inspection(string operationRunId, string outcome, decimal value)
        => new()
        {
            RecordId = Guid.CreateVersion7(),
            WorkpieceId = $"wp-{operationRunId}-{value}",
            OperationRunId = operationRunId,
            DefinitionCode = "surface",
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

    private sealed class FilteringEventReader(IReadOnlyList<CentralProductionEvent> rows) : IChatEventReader
    {
        public Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
            string actorId,
            CentralEventQuery query,
            CancellationToken ct = default)
        {
            IEnumerable<CentralProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.CorrelationId))
                filtered = filtered.Where(row => row.Event.CorrelationId == query.CorrelationId);
            foreach (var pair in query.Context)
                filtered = filtered.Where(row =>
                    row.Event.Context.TryGetValue(pair.Key, out var value) &&
                    string.Equals(value, pair.Value, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<CentralProductionEvent>>(filtered.Take(query.Limit).ToArray());
        }

        public Task<CentralEventScopeStats> GetScopeStatsAsync(
            string actorId,
            CentralEventQuery query,
            CancellationToken ct = default)
        {
            var filtered = QueryAsync(actorId, query with { Limit = 500 }, ct).Result;
            return Task.FromResult(new CentralEventScopeStats
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
                .Where(record => string.IsNullOrWhiteSpace(query.OperationRunId) ||
                                 record.OperationRunId == query.OperationRunId)
                .Take(query.Limit)
                .ToArray());
    }
}

