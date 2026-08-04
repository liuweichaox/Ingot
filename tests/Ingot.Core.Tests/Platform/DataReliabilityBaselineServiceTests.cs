using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class DataReliabilityBaselineServiceTests
{
    [Fact]
    public async Task CalculateAsync_ShouldUseExplicitAdmissionRulesAndDefinitions()
    {
        var now = DateTimeOffset.UtcNow;
        var completed = new[]
        {
            Completed(1, "RUN-GOOD", now.AddMinutes(-2)),
            Completed(2, "RUN-BAD", now.AddMinutes(-1))
        };
        var good = new CycleComparisonRow
        {
            CorrelationId = "RUN-GOOD",
            MachineId = "PRESS-01",
            ProductSeries = "lens",
            StartedAt = now.AddMinutes(-3),
            CompletedAt = now.AddMinutes(-2),
            HasStarted = true,
            HasCompleted = true,
            LifecycleComplete = true,
            Context = new Dictionary<string, string>
            {
                ["context_capture_status"] = "resolved",
                ["equipment_id"] = "PRESS-01",
                ["operation_run_id"] = "RUN-GOOD",
                ["material_lot"] = "LOT-01"
            },
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 100,
                MaximumGapMs = 1200,
                MaximumAbsoluteSourceClockOffsetMs = 45,
                P95PlatformIngestLatencyMs = 900,
                MaximumPlatformIngestLatencyMs = 1500,
                NegativePlatformIngestLatencyCount = 1
            },
            RecipeParameters =
            [
                new CycleRecipeParameter
                {
                    Code = "temperature.actual",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(500d)
                }
            ],
            InspectionOutcomes = ["PASS"]
        };
        var bad = new CycleComparisonRow
        {
            CorrelationId = "RUN-BAD",
            MachineId = "PRESS-02",
            ProductSeries = "lens",
            StartedAt = now.AddMinutes(-1),
            HasCompleted = true,
            Context = new Dictionary<string, string>
            {
                ["context_capture_status"] = "configuration_missing",
                ["equipment_id"] = "PRESS-02"
            },
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Unavailable,
                SequenceGapCount = 2
            }
        };
        var service = new DataReliabilityBaselineService(
            new FakeEventStore(completed),
            new FakeCycleService(new Dictionary<string, CycleComparisonRow>
            {
                [good.CorrelationId] = good,
                [bad.CorrelationId] = bad
            }));

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery());

        Assert.Equal(2, baseline.MatchingCompletedRunCount);
        Assert.Equal(2, baseline.AnalyzedRunCount);
        Assert.False(baseline.Truncated);
        Assert.Equal(0.5, Rate(baseline, "process_data_completeness"));
        Assert.Equal(0.5, Rate(baseline, "actual_parameter_coverage"));
        Assert.Equal(0.5, Rate(baseline, "context_capture_integrity"));
        Assert.Equal(0.5, Rate(baseline, "minimal_context_coverage"));
        Assert.Equal(0.5, Rate(baseline, "run_quality_association"));
        Assert.Equal(0.5, Rate(baseline, "analysis_admission"));
        Assert.Equal(2, baseline.SequenceGapCount);
        Assert.Equal(45, baseline.MaximumAbsoluteSourceClockOffsetMs);
        Assert.Equal(900, baseline.WorstRunP95PlatformIngestLatencyMs);
        Assert.Equal(1500, baseline.MaximumPlatformIngestLatencyMs);
        Assert.Equal(1, baseline.NegativePlatformIngestLatencyCount);
        Assert.Equal(0.5, Assert.Single(
            baseline.ContextFields,
            item => item.Field == "operation_run_id").Coverage);
        Assert.Equal(0.5, Assert.Single(
            baseline.ContextFields,
            item => item.Field == "material_lot_ref").Coverage);
        Assert.Contains(baseline.Exclusions, item =>
            item.Code == "actual_parameters_missing" && item.RunCount == 1);
        Assert.Contains(baseline.Exclusions, item =>
            item.Code == "context_capture_invalid" && item.RunCount == 1);
        Assert.Contains(
            "不使用配方计划值",
            Assert.Single(baseline.Rates, item => item.Code == "actual_parameter_coverage").Definition);
    }

    [Fact]
    public async Task CalculateAsync_ShouldExposeStrataAndDetectConfoundedFactors()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new[]
        {
            FactorRow("RUN-1", "PRESS-1", "TOOL-1", "LOT-1", "PASS", now.AddMinutes(-4)),
            FactorRow("RUN-2", "PRESS-1", "TOOL-2", "LOT-1", "FAIL", now.AddMinutes(-3)),
            FactorRow("RUN-3", "PRESS-2", "TOOL-1", "LOT-2", "FAIL", now.AddMinutes(-2)),
            FactorRow("RUN-4", "PRESS-2", "TOOL-2", "LOT-2", "PASS", now.AddMinutes(-1))
        };
        var completed = rows.Select((row, index) =>
            Completed(index + 1, row.CorrelationId, row.CompletedAt!.Value)).ToArray();
        var service = new DataReliabilityBaselineService(
            new FakeEventStore(completed),
            new FakeCycleService(rows.ToDictionary(static row => row.CorrelationId)));

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery());

        var equipment = Assert.Single(baseline.ContextFactors, item => item.Field == "equipment_id");
        Assert.Equal(2, equipment.DistinctLevelCount);
        Assert.All(equipment.Levels, static level => Assert.Equal(2, level.RunCount));
        var tooling = Assert.Single(baseline.ContextFactors, item => item.Field == "tooling_id");
        Assert.All(tooling.Levels, static level =>
        {
            Assert.Equal(1, level.PassRunCount);
            Assert.Equal(1, level.FailRunCount);
        });
        var equipmentTooling = Assert.Single(baseline.ContextFactorOverlaps, item =>
            item.LeftField == "equipment_id" && item.RightField == "tooling_id");
        Assert.Equal("overlapping", equipmentTooling.Identifiability);
        Assert.Equal(1, equipmentTooling.OverlapRate);
        var equipmentMaterial = Assert.Single(baseline.ContextFactorOverlaps, item =>
            item.LeftField == "equipment_id" && item.RightField == "material_lot_ref");
        Assert.Equal("confounded", equipmentMaterial.Identifiability);
        Assert.Equal(0.5, equipmentMaterial.OverlapRate);
        Assert.Equal(1, baseline.UnidentifiableConfoundingCount);
    }

    private static double? Rate(DataReliabilityBaseline value, string code)
        => Assert.Single(value.Rates, item => item.Code == code).Rate;

    private static PlatformProductionEvent Completed(long ingestId, string correlationId, DateTimeOffset at)
        => new()
        {
            IngestId = ingestId,
            EdgeId = "EDGE-001",
            IngestedAt = at,
            Event = ProductionEvent.Create(
                "cycle.completed",
                at,
                "edge/EDGE-001/equipment/PRESS-01",
                new ObjectRef("equipment", "PRESS-01"),
                correlationId) with
            {
                Seq = ingestId
            }
        };

    private static CycleComparisonRow FactorRow(
        string correlationId,
        string equipmentId,
        string toolingId,
        string materialLot,
        string outcome,
        DateTimeOffset completedAt)
        => new()
        {
            CorrelationId = correlationId,
            MachineId = equipmentId,
            ProductSeries = "demo",
            StartedAt = completedAt.AddMinutes(-1),
            CompletedAt = completedAt,
            HasStarted = true,
            HasCompleted = true,
            LifecycleComplete = true,
            Context = new Dictionary<string, string>
            {
                ["equipment_id"] = equipmentId,
                ["operation_run_id"] = correlationId,
                ["tooling_id"] = toolingId,
                ["material_lot_ref"] = materialLot
            },
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 10
            },
            InspectionOutcomes = [outcome]
        };

    private sealed class FakeCycleService(
        IReadOnlyDictionary<string, CycleComparisonRow> rows) : ICycleComparisonService
    {
        public Task<CycleComparisonRow?> GetCycleAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(rows.GetValueOrDefault(correlationId));

        public Task<IReadOnlyDictionary<string, CycleComparisonRow>> GetCyclesAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, CycleComparisonRow>>(
                correlationIds.Where(rows.ContainsKey)
                    .ToDictionary(id => id, id => rows[id], StringComparer.Ordinal));

        public Task<CycleComparisonResult?> CompareWithHistoryAsync(
            string correlationId,
            int limit,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CycleComparisonResult?> CompareSelectedAsync(
            string baselineCycleId,
            IReadOnlyList<string> cycleIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeEventStore(
        IReadOnlyList<PlatformProductionEvent> rows) : IPlatformEventStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(rows
                .Where(row => query.EventType is null || row.Event.EventType == query.EventType)
                .Where(row => !query.AfterIngestId.HasValue || row.IngestId > query.AfterIngestId)
                .OrderBy(static row => row.IngestId)
                .Take(query.Limit)
                .ToArray());

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new PlatformEventScopeStats { Count = rows.Count });

        public Task<bool> CanConnectAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
