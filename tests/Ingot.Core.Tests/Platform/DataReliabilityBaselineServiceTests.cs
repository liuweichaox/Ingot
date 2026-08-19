using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessExecutions;
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
        var good = new ExecutionComparisonRow
        {
            ExecutionId = "RUN-GOOD",
            EquipmentId = "PRESS-01",
            ProductFamilyCode = "lens",
            StartedAt = now.AddMinutes(-3),
            CompletedAt = now.AddMinutes(-2),
            HasStarted = true,
            HasCompleted = true,
            LifecycleComplete = true,
            Context = new Dictionary<string, string>
            {
                ["context_capture_status"] = "resolved",
                ["equipment_id"] = "PRESS-01",
                ["execution_id"] = "RUN-GOOD",
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
            ControlParameters =
            [
                new ExecutionControlParameterValue
                {
                    Code = "temperature.actual",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(500d)
                }
            ],
            InspectionOutcomes = ["PASS"]
        };
        var bad = new ExecutionComparisonRow
        {
            ExecutionId = "RUN-BAD",
            EquipmentId = "PRESS-02",
            ProductFamilyCode = "lens",
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
            new FakeProcessExecutionService(new Dictionary<string, ExecutionComparisonRow>
            {
                [good.ExecutionId] = good,
                [bad.ExecutionId] = bad
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
            item => item.Field == "execution_id").Coverage);
        Assert.Equal(0.5, Assert.Single(
            baseline.ContextFields,
            item => item.Field == "material_lot_ref").Coverage);
        Assert.Equal(
            baseline.ContextFields.Count,
            baseline.ContextFields.Select(static item => item.Field).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(baseline.Exclusions, item =>
            item.Code == "actual_parameters_missing" && item.RunCount == 1);
        Assert.Contains(baseline.Exclusions, item =>
            item.Code == "context_capture_invalid" && item.RunCount == 1);
        Assert.Contains(
            "不使用工艺规范计划值",
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
            Completed(index + 1, row.ExecutionId, row.CompletedAt!.Value)).ToArray();
        var service = new DataReliabilityBaselineService(
            new FakeEventStore(completed),
            new FakeProcessExecutionService(rows.ToDictionary(static row => row.ExecutionId)));

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery());

        var equipment = Assert.Single(baseline.ContextFactors, item => item.Field == "equipment_id");
        Assert.Equal(2, equipment.DistinctLevelCount);
        Assert.All(equipment.Levels, static level => Assert.Equal(2, level.RunCount));
        var tooling = Assert.Single(baseline.ContextFactors, item => item.Field == "tooling_assembly_id");
        Assert.All(tooling.Levels, static level =>
        {
            Assert.Equal(1, level.PassRunCount);
            Assert.Equal(1, level.FailRunCount);
        });
        var equipmentTooling = Assert.Single(baseline.ContextFactorOverlaps, item =>
            item.LeftField == "equipment_id" && item.RightField == "tooling_assembly_id");
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

    private static PlatformProductionEvent Completed(long ingestId, string executionId, DateTimeOffset at)
        => new()
        {
            IngestId = ingestId,
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            IngestedAt = at,
            Event = ProductionEvent.Create(
                "process.execution.completed",
                at,
                "edge/EDGE-001/equipment/PRESS-01",
                new ObjectRef("equipment", "PRESS-01"),
                executionId) with
            {
                Seq = ingestId
            }
        };

    private static ExecutionComparisonRow FactorRow(
        string executionId,
        string equipmentId,
        string toolingAssemblyId,
        string materialLot,
        string outcome,
        DateTimeOffset completedAt)
        => new()
        {
            ExecutionId = executionId,
            EquipmentId = equipmentId,
            ProductFamilyCode = "demo",
            StartedAt = completedAt.AddMinutes(-1),
            CompletedAt = completedAt,
            HasStarted = true,
            HasCompleted = true,
            LifecycleComplete = true,
            Context = new Dictionary<string, string>
            {
                ["equipment_id"] = equipmentId,
                ["execution_id"] = executionId,
                ["tooling_assembly_id"] = toolingAssemblyId,
                ["material_lot_ref"] = materialLot
            },
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 10
            },
            InspectionOutcomes = [outcome]
        };

    private sealed class FakeProcessExecutionService(
        IReadOnlyDictionary<string, ExecutionComparisonRow> rows) : IExecutionComparisonService
    {
        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(string executionId, CancellationToken ct = default)
            => Task.FromResult(rows.GetValueOrDefault(executionId));

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                executionIds.Where(rows.ContainsKey)
                    .ToDictionary(id => id, id => rows[id], StringComparer.Ordinal));

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
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
