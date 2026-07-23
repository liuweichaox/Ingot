using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class QualityWorkflowTests
{
    [Fact]
    public async Task WorkflowDoesNotInventTasksWhenNoPublishedPlanMatches()
    {
        var events = new FakeEventStore(
        [
            Row(1, Event("cycle.completed", "CYCLE-1", "WP-1", "UNCONFIGURED", DateTimeOffset.Parse("2026-07-20T08:10:00Z")))
        ]);
        var workflow = new InspectionWorkflowService(
            events,
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore([]));

        var tasks = await workflow.QueryTasksAsync("all", 100);

        Assert.Empty(tasks);
    }

    [Fact]
    public void InspectionPlanValidationMakesReviewEvidenceExplicit()
    {
        var plan = new InspectionPlan
        {
            PlanId = "QUALITY.GENERAL",
            Version = 2,
            Name = "通用质量方案",
            Status = "PUBLISHED",
            Scope = new InspectionPlanScope
            {
                ProductSeries = "SERIES-A",
                ContextSelector = new Dictionary<string, string> { ["material_grade"] = "A-01" }
            },
            Items =
            [
                new InspectionPlanItem
                {
                    DefinitionCode = "visual.final",
                    DefinitionVersion = 1,
                    Required = true,
                    RequiresReview = true
                }
            ]
        };

        var valid = InspectionMasterDataValidator.TryValidate(plan, out var normalized, out var error);

        Assert.True(valid, error);
        Assert.Equal("quality.general", normalized!.PlanId);
        Assert.Equal("series-a", normalized.Scope.ProductSeries);
        Assert.Equal("A-01", normalized.Scope.ContextSelector["material_grade"]);
        Assert.True(normalized.Items[0].RequiresAttachment);
    }

    [Fact]
    public async Task WorkflowDerivesPendingAndReviewPendingTasksFromCompletedCycles()
    {
        var events = new FakeEventStore(
        [
            Row(1, Event("cycle.completed", "CYCLE-1", "WP-1", "LENS-A", DateTimeOffset.Parse("2026-07-20T08:10:00Z"))),
            Row(2, Event("cycle.completed", "CYCLE-2", "WP-2", "LENS-A", DateTimeOffset.Parse("2026-07-20T08:20:00Z")))
        ]);
        var visual = Inspection("CYCLE-2", "WP-2", "optical.appearance.machine", withAttachment: true);
        var manual = Inspection("CYCLE-2", "WP-2", "optical.final.manual", withAttachment: false);
        var workflow = new InspectionWorkflowService(
            events,
            new FakeInspectionStore([visual, manual]),
            new FakeReviewStore(),
            new FakeMasterDataStore());

        var tasks = await workflow.QueryTasksAsync("all", 100);

        Assert.Equal(2, tasks.Count);
        Assert.Equal("review_pending", tasks[0].Status);
        Assert.Equal("CYCLE-2", tasks[0].OperationRunId);
        Assert.Equal("pending", tasks[1].Status);
        Assert.Equal(2, tasks[1].MissingDefinitionCodes.Count);
    }

    [Fact]
    public async Task ComparisonReadsEveryPageAndComputesCompleteSameSeriesCycles()
    {
        var rows = new List<PlatformProductionEvent>();
        AddCycle(rows, "BASE", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddCycle(rows, "HISTORY", "LENS-A", "PRESS-02", DateTimeOffset.Parse("2026-07-20T07:00:00Z"), rows.Count + 1);
        AddCycle(rows, "OTHER", "LENS-B", "PRESS-01", DateTimeOffset.Parse("2026-07-20T06:00:00Z"), rows.Count + 1);
        var baselineVisual = Inspection("BASE", "WP-BASE", "optical.appearance.machine", withAttachment: true);
        var historyManual = Inspection("HISTORY", "WP-HISTORY", "optical.final.manual", withAttachment: false);
        var review = new InspectionReview
        {
            ReviewId = Guid.CreateVersion7(),
            InspectionRecordId = baselineVisual.RecordId,
            OperationRunId = "BASE",
            Decision = InspectionReviewDecisions.Confirmed,
            ReviewedAt = DateTimeOffset.UtcNow,
            ReviewedBy = "reviewer"
        };
        var reviewStore = new FakeReviewStore(new Dictionary<Guid, InspectionReview> { [baselineVisual.RecordId] = review });
        var service = new CycleComparisonService(
            new FakeEventStore(rows),
            new FakeInspectionStore([baselineVisual, historyManual]),
            reviewStore,
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.CompareWithHistoryAsync("BASE", 10);

        Assert.NotNull(result);
        Assert.Equal("LENS-A", result.ProductSeries);
        Assert.Equal(600, result.Baseline.SampleCount);
        Assert.Equal(1d, result.Baseline.SampleCompleteness);
        Assert.Equal(5, result.Baseline.PhaseCount);
        Assert.Equal(InspectionReviewDecisions.Confirmed, result.Baseline.VisualReviewDecision);
        Assert.Single(result.HistoricalCycles);
        Assert.Equal("HISTORY", result.HistoricalCycles[0].CorrelationId);
        Assert.Equal(600, result.Baseline.Signals.Single(item => item.Code == "press.load").SampleCount);
        Assert.Equal(2, result.Acceptance.CompleteCycleCount);
        Assert.Equal(2, result.Acceptance.PhaseCompleteCycleCount);

        var selected = await service.CompareSelectedAsync("HISTORY", ["BASE", "HISTORY"]);
        Assert.NotNull(selected);
        Assert.Equal("HISTORY", selected.BaselineCycleId);
        Assert.Equal("HISTORY", selected.Baseline.CorrelationId);
        Assert.Equal("BASE", Assert.Single(selected.HistoricalCycles).CorrelationId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompareSelectedAsync("BASE", ["BASE", "OTHER"]));
    }

    [Fact]
    public async Task CycleRecordsKeepAllSamplesAndUseConfiguredPhaseAndQualityRules()
    {
        var rows = new List<PlatformProductionEvent>();
        AddCycle(rows, "CYCLE-RECORD", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        var service = new CycleRecordService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.QueryAsync(
            null, null, "LENS-A", null, null, null, null, null, "completed", 100);

        var cycle = Assert.Single(result.Data);
        Assert.Equal(600, cycle.SampleCount);
        Assert.Equal(1d, cycle.SampleCompleteness);
        Assert.True(cycle.PhaseComplete);
        Assert.Equal(5, cycle.Phases.Count);
        Assert.Equal("PENDING", cycle.QualityStatus);
        Assert.Equal(2, cycle.RequiredInspectionCount);
        Assert.Empty(cycle.DataIssues);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Overview.SampleCompleteCount);
    }

    [Fact]
    public async Task CycleRecordPaginationReturnsFilteredTotalBeyondCurrentPage()
    {
        var rows = new List<PlatformProductionEvent>();
        AddCycle(rows, "PAGE-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddCycle(rows, "PAGE-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T09:00:00Z"), 2);
        var service = new CycleRecordService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.QueryAsync(
            null, null, "LENS-A", null, null, null, null, null, "completed", 1, 1);

        Assert.Single(result.Data);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Overview.CycleCount);
        Assert.Equal(2, result.Overview.CompletedCount);
    }

    [Fact]
    public async Task ContinuousProcessWindowsCompareWithoutCycleCorrelationSemantics()
    {
        var rows = new List<PlatformProductionEvent>();
        AddCycle(rows, "RUN-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        // A previous product may complete exactly when the selected window starts.
        // Window context must come from its process samples, not that boundary event.
        rows.Add(Row(rows.Count + 1, Event(
            "cycle.completed", "PRIOR-B", "WP-PRIOR-B", "LENS-B",
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"), "PRESS-01")));
        AddCycle(rows, "RUN-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T10:00:00Z"), rows.Count + 1);
        var service = new ProcessWindowComparisonService(
            new FakeEventStore(rows),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()),
            new FakeInspectionStore([]));

        var result = await service.CompareAsync(new ProcessWindowComparisonRequest
        {
            BaselineWindowId = "morning-a",
            Windows =
            [
                new() { WindowId = "morning-a", SubjectType = "optical-molding-machine", SubjectId = "PRESS-01", From = DateTimeOffset.Parse("2026-07-20T08:00:00Z"), To = DateTimeOffset.Parse("2026-07-20T08:10:01Z") },
                new() { WindowId = "morning-b", SubjectType = "optical-molding-machine", SubjectId = "PRESS-01", From = DateTimeOffset.Parse("2026-07-20T10:00:00Z"), To = DateTimeOffset.Parse("2026-07-20T10:10:01Z") }
            ]
        });

        Assert.Equal("window-comparison", result.AnalysisPlanId);
        Assert.Equal(600, result.Baseline.SampleCount);
        Assert.Equal(5, result.Baseline.Signals.Count);
        Assert.Single(result.ComparisonWindows);
    }

    [Fact]
    public async Task ContinuousProcessWindowsIncludeQualityScopesAndMeasurements()
    {
        var rows = new List<PlatformProductionEvent>();
        AddCycle(rows, "RUN-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddCycle(rows, "RUN-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T10:00:00Z"), rows.Count + 1);
        var quality = Inspection("quality-window-a", "WP-A", "optical.final.manual", false) with
        {
            Measurements =
            [
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = "center.thickness",
                    Outcome = "PASS",
                    NumericValue = 2.15m,
                    Unit = "mm"
                }
            ]
        };
        InspectionScope[] scopes =
        [
            new()
            {
                ScopeId = "quality-window-a",
                ScopeType = "analysis-window",
                WorkpieceId = "WP-A",
                SubjectType = "optical-molding-machine",
                SubjectId = "PRESS-01",
                ProductSeries = "LENS-A",
                InspectionPlanId = "lens.quality",
                From = DateTimeOffset.Parse("2026-07-20T08:00:00Z"),
                To = DateTimeOffset.Parse("2026-07-20T08:10:01Z")
            }
        ];
        var service = new ProcessWindowComparisonService(
            new FakeEventStore(rows),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()),
            new FakeInspectionStore([quality], scopes));

        var result = await service.CompareAsync(new ProcessWindowComparisonRequest
        {
            BaselineWindowId = "morning-a",
            Windows =
            [
                new() { WindowId = "morning-a", SubjectType = "optical-molding-machine", SubjectId = "PRESS-01", From = DateTimeOffset.Parse("2026-07-20T08:00:00Z"), To = DateTimeOffset.Parse("2026-07-20T08:10:01Z") },
                new() { WindowId = "morning-b", SubjectType = "optical-molding-machine", SubjectId = "PRESS-01", From = DateTimeOffset.Parse("2026-07-20T10:00:00Z"), To = DateTimeOffset.Parse("2026-07-20T10:10:01Z") }
            ]
        });

        Assert.Equal(1, result.Baseline.Quality.ScopeCount);
        Assert.Equal(1, result.Baseline.Quality.InspectionCount);
        Assert.Equal(1d, result.Baseline.Quality.PassRate);
        Assert.Equal(2.15d, result.Baseline.Quality.Characteristics.Single().Average);
        Assert.Equal(0, result.ComparisonWindows.Single().Quality.InspectionCount);
    }

    private static void AddCycle(
        ICollection<PlatformProductionEvent> rows,
        string cycleId,
        string productSeries,
        string machineId,
        DateTimeOffset start,
        int firstIngestId)
    {
        var ingestId = firstIngestId;
        rows.Add(Row(ingestId++, Event(
            "cycle.started",
            cycleId,
            $"WP-{cycleId}",
            productSeries,
            start,
            machineId,
            new Dictionary<string, object?> { ["expectedSampleCount"] = 600 })));
        for (var second = 0; second < 600; second++)
        {
            var phase = second switch { < 90 => "10", < 240 => "20", < 360 => "30", < 480 => "40", _ => "50" };
            var evt = Event(
                "process.sample",
                cycleId,
                $"WP-{cycleId}",
                productSeries,
                start.AddSeconds(second),
                machineId,
                new Dictionary<string, object?>
                {
                    ["values"] = new Dictionary<string, object?>
                    {
                        ["upper_mold.ir_temperature"] = 600d + second / 100d,
                        ["lower_mold.ir_temperature"] = 595d + second / 100d,
                        ["press.load"] = 120d,
                        ["chamber.vacuum"] = 12d,
                        ["servo.position"] = 12.5d
                    }
                });
            evt = evt with { Context = new Dictionary<string, string>(evt.Context) { ["recipe_step"] = phase } };
            rows.Add(Row(ingestId++, evt));
        }
        rows.Add(Row(ingestId, Event(
            "cycle.completed",
            cycleId,
            $"WP-{cycleId}",
            productSeries,
            start.AddMinutes(10),
            machineId)));
    }

    private static ProductionEvent Event(
        string type,
        string cycleId,
        string workpieceId,
        string productSeries,
        DateTimeOffset occurredAt,
        string machineId = "PRESS-01",
        IReadOnlyDictionary<string, object?>? data = null)
        => new()
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = type,
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            Source = "edge/EDGE-001/PLC-01/test",
            Subject = new ObjectRef("optical-molding-machine", machineId),
            CorrelationId = cycleId,
            Seq = 1,
            Context = new Dictionary<string, string>
            {
                ["workpiece_id"] = workpieceId,
                ["product_series"] = productSeries,
                ["product_code"] = $"{productSeries}-01",
                ["recipe_id"] = $"RCP-{productSeries}",
                ["recipe_version"] = "1",
                ["data_model_id"] = "optical-molding",
                ["data_model_version"] = "1"
            },
            Data = data ?? new Dictionary<string, object?>()
        };

    private static PlatformProductionEvent Row(long ingestId, ProductionEvent evt)
        => new() { IngestId = ingestId, EdgeId = "EDGE-001", IngestedAt = evt.RecordedAt, Event = evt };

    private static InspectionRecord Inspection(string cycleId, string workpieceId, string definitionCode, bool withAttachment)
        => new()
        {
            RecordId = Guid.CreateVersion7(),
            WorkpieceId = workpieceId,
            OperationRunId = cycleId,
            DefinitionCode = definitionCode,
            DefinitionVersion = 1,
            MeasuredAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow,
            IngestedAt = DateTimeOffset.UtcNow,
            Outcome = "PASS",
            SubmittedBy = "operator",
            SubmitterVerified = true,
            Measurements = [],
            Attachments = withAttachment
                ?
                [
                    new InspectionAttachment
                    {
                        AttachmentId = Guid.CreateVersion7(),
                        StorageRef = "attachment://sha256/test/original.bmp",
                        Sha256 = new string('a', 64),
                        MediaType = "image/bmp",
                        FileName = "original.bmp",
                        SizeBytes = 100
                    }
                ]
                : []
        };

    private sealed class FakeEventStore(IReadOnlyList<PlatformProductionEvent> rows) : IPlatformEventStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(PlatformEventQuery query, CancellationToken ct = default)
        {
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.EventType)) filtered = filtered.Where(item => item.Event.EventType == query.EventType);
            if (!string.IsNullOrWhiteSpace(query.SubjectType)) filtered = filtered.Where(item => item.Event.Subject.Type == query.SubjectType);
            if (!string.IsNullOrWhiteSpace(query.SubjectId)) filtered = filtered.Where(item => item.Event.Subject.Id == query.SubjectId);
            if (!string.IsNullOrWhiteSpace(query.CorrelationId)) filtered = filtered.Where(item => item.Event.CorrelationId == query.CorrelationId);
            if (query.From.HasValue) filtered = filtered.Where(item => item.Event.OccurredAt >= query.From.Value);
            if (query.To.HasValue) filtered = filtered.Where(item => item.Event.OccurredAt <= query.To.Value);
            foreach (var pair in query.Context) filtered = filtered.Where(item => item.Event.Context.GetValueOrDefault(pair.Key) == pair.Value);
            if (query.AfterIngestId.HasValue) filtered = filtered.Where(item => item.IngestId > query.AfterIngestId.Value);
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(filtered.OrderBy(item => item.IngestId).Take(query.Limit).ToArray());
        }
        public Task<PlatformEventScopeStats> GetScopeStatsAsync(PlatformEventQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CanConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeInspectionStore(
        IReadOnlyList<InspectionRecord> records,
        IReadOnlyList<InspectionScope>? scopes = null) : IInspectionRecordStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionRecordResult> CreateAsync(CreateInspectionRecordRequest request, bool submitterVerified, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default) => Task.FromResult(records.FirstOrDefault(item => item.RecordId == recordId));
        public Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionScope>>(scopes ?? []);
        public Task<IReadOnlyList<InspectionRecord>> QueryAsync(InspectionRecordQuery query, CancellationToken ct = default) => Task.FromResult(records);
        public Task<IReadOnlyList<InspectionRecord>> QueryAllByOperationRunIdsAsync(IReadOnlyCollection<string> operationRunIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(records.Where(item => operationRunIds.Contains(item.OperationRunId)).ToArray());
    }

    private sealed class FakeReviewStore(IReadOnlyDictionary<Guid, InspectionReview>? latest = null) : IInspectionReviewStore
    {
        private readonly IReadOnlyDictionary<Guid, InspectionReview> _latest = latest ?? new Dictionary<Guid, InspectionReview>();
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string operationRunId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => Task.FromResult(_latest.Values.FirstOrDefault(item => item.ReviewId == reviewId));
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? operationRunId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionReview>>(_latest.Values.ToArray());
        public Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(IReadOnlyCollection<Guid> inspectionRecordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, InspectionReview>>(_latest.Where(pair => inspectionRecordIds.Contains(pair.Key)).ToDictionary());
        public Task LogAccessAsync(Guid? inspectionRecordId, Guid? attachmentId, string action, string actor, string? detail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(Guid? inspectionRecordId, Guid? attachmentId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionAuditEntry>>([]);
    }

    private sealed class FakeMasterDataStore : IInspectionMasterDataStore
    {
        private readonly IReadOnlyList<InspectionPlan> _plans;

        public FakeMasterDataStore(IReadOnlyList<InspectionPlan>? plans = null)
        {
            _plans = plans ?? [Plan];
        }

        private static readonly InspectionPlan Plan = new()
        {
            PlanId = "lens.quality",
            Version = 1,
            Name = "镜片质量方案",
            Status = InspectionPlanStatuses.Published,
            Priority = 10,
            Scope = new InspectionPlanScope { ProductSeries = "lens-a" },
            UpdatedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new InspectionPlanItem { DefinitionCode = "optical.appearance.machine", DefinitionVersion = 1, Sequence = 10, Required = true, RequiresAttachment = true, RequiresReview = true },
                new InspectionPlanItem { DefinitionCode = "optical.final.manual", DefinitionVersion = 1, Sequence = 20, Required = true }
            ]
        };

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<InspectionDefinition> UpsertInspectionDefinitionAsync(InspectionDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionDefinition>> ListInspectionDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionDefinition>>([]);
        public Task<InspectionDefinition?> GetInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => Task.FromResult<InspectionDefinition?>(null);
        public Task<bool> DeleteInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionPlan> UpsertInspectionPlanAsync(InspectionPlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionPlan>> ListInspectionPlansAsync(CancellationToken ct = default) => Task.FromResult(_plans);
        public Task<InspectionPlan?> GetInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<InspectionPlan?>(Plan);
        public Task<bool> DeleteInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PhaseDefinition> UpsertPhaseDefinitionAsync(PhaseDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseDefinition>> ListPhaseDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseDefinition>>(
            [
                new() { Code = "preheat", Name = "预热", Required = true },
                new() { Code = "soak", Name = "均热", Required = true },
                new() { Code = "press", Name = "压制", Required = true },
                new() { Code = "anneal", Name = "退火", Required = true },
                new() { Code = "cool", Name = "冷却", Required = true }
            ]);
        public Task<PhaseDefinition?> GetPhaseDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<PhaseDefinition?>(null);
        public Task<bool> DeletePhaseDefinitionAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PhaseMapping> UpsertPhaseMappingAsync(PhaseMapping mapping, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseMapping>>(
            new[] { ("10", "preheat"), ("20", "soak"), ("30", "press"), ("40", "anneal"), ("50", "cool") }
                .Select(item => new PhaseMapping
                {
                    MappingId = $"test-{item.Item1}", RecipeId = "RCP-LENS-A", RecipeStep = item.Item1, PhaseCode = item.Item2
                }).ToArray());
        public Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default) => Task.FromResult<PhaseMapping?>(null);
        public Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeatureDefinition>>(
            [new() { Code = "comparison.press.load", Name = "压力", PhaseCode = "cycle", Signal = "press.load", Aggregation = "mean", Unit = "kg", Enabled = true, UseInComparison = true }]);
        public Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<FeatureDefinition?>(null);
        public Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProcessConfigurationStore : IProcessConfigurationStore
    {
        private static readonly ProcessDataModel Model = new()
        {
            ModelId = "optical-molding",
            Version = 1,
            Name = "光学模压",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                StepSourceKey = "recipe_step",
                DataItems =
                [
                    new() { Code = "upper_mold.ir_temperature", SourceField = "上模温度", Unit = "Cel" },
                    new() { Code = "lower_mold.ir_temperature", SourceField = "下模温度", Unit = "Cel" },
                    new() { Code = "press.load", SourceField = "压力", Unit = "kg" },
                    new() { Code = "chamber.vacuum", SourceField = "真空度", Unit = "kPa" },
                    new() { Code = "servo.position", SourceField = "伺服位置", Unit = "mm" }
                ]
            },
            Stages =
            [
                new() { SourceStep = "10", Code = "preheat", Name = "预热" },
                new() { SourceStep = "20", Code = "soak", Name = "均热" },
                new() { SourceStep = "30", Code = "press", Name = "压制" },
                new() { SourceStep = "40", Code = "anneal", Name = "退火" },
                new() { SourceStep = "50", Code = "cool", Name = "冷却" }
            ]
        };

        private static readonly ProcessAnalysisPlan Plan = new()
        {
            PlanId = "cycle-comparison",
            Version = 1,
            Name = "周期对比",
            Status = ConfigurationStatuses.Published,
            DataModelId = Model.ModelId,
            DataModelVersion = Model.Version,
            ComparisonKeys = ["product_series"],
            Signals = Model.Acquisition.DataItems.Select(static item =>
                new AnalysisSignalSelection { DataItemCode = item.Code, Features = ["mean", "min", "max"] }).ToArray()
        };

        private static readonly ProcessAnalysisPlan WindowPlan = Plan with
        {
            PlanId = "window-comparison",
            Name = "窗口对比",
            AnalysisScope = "analysis-window"
        };

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessDataModel>>([Model]);
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult<ProcessDataModel?>(Model);
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RecipeVersion> UpsertRecipeVersionAsync(RecipeVersion value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RecipeVersion>> ListRecipeVersionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RecipeVersion>>([]);
        public Task<RecipeVersion?> GetRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default) => Task.FromResult<RecipeVersion?>(null);
        public Task<bool> DeleteRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([Plan, WindowPlan]);
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<ProcessAnalysisPlan?>(Plan);
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
