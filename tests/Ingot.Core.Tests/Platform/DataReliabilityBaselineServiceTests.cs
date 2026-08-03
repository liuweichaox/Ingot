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
                ["equipment_id"] = "PRESS-01",
                ["operation_run_id"] = "RUN-GOOD",
                ["material_lot"] = "LOT-01"
            },
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 100,
                MaximumGapMs = 1200
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
            Context = new Dictionary<string, string> { ["equipment_id"] = "PRESS-02" },
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
        Assert.Equal(0.5, Rate(baseline, "minimal_context_coverage"));
        Assert.Equal(0.5, Rate(baseline, "run_quality_association"));
        Assert.Equal(0.5, Rate(baseline, "analysis_admission"));
        Assert.Equal(2, baseline.SequenceGapCount);
        Assert.Equal(0.5, Assert.Single(
            baseline.ContextFields,
            item => item.Field == "operation_run_id").Coverage);
        Assert.Contains(baseline.Exclusions, item =>
            item.Code == "actual_parameters_missing" && item.RunCount == 1);
        Assert.Contains(
            "不使用配方计划值",
            Assert.Single(baseline.Rates, item => item.Code == "actual_parameter_coverage").Definition);
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
