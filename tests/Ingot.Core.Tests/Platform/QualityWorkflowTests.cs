using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
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
            Row(1, Event("process.execution.completed", "CYCLE-1", "WP-1", "UNCONFIGURED", DateTimeOffset.Parse("2026-07-20T08:10:00Z")))
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
                ProductFamilyCode = "SERIES-A",
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
        Assert.Equal("series-a", normalized.Scope.ProductFamilyCode);
        Assert.Equal("A-01", normalized.Scope.ContextSelector["material_grade"]);
        Assert.True(normalized.Items[0].RequiresAttachment);
    }

    [Fact]
    public async Task WorkflowDerivesPendingAndReviewPendingTasksFromCompletedProcessExecutions()
    {
        var events = new FakeEventStore(
        [
            Row(1, Event("process.execution.completed", "CYCLE-1", "WP-1", "LENS-A", DateTimeOffset.Parse("2026-07-20T08:10:00Z"))),
            Row(2, Event("process.execution.completed", "CYCLE-2", "WP-2", "LENS-A", DateTimeOffset.Parse("2026-07-20T08:20:00Z")))
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
        Assert.Equal("CYCLE-2", tasks[0].ExecutionId);
        Assert.Equal("pending", tasks[1].Status);
        Assert.Equal(2, tasks[1].MissingDefinitionCodes.Count);
    }

    [Fact]
    public async Task ComparisonReadsEveryPageAndComputesCompleteSameSeriesProcessExecutions()
    {
        var rows = new List<PlatformProductionEvent>();
        AddProcessExecution(rows, "BASE", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddProcessExecution(rows, "HISTORY", "LENS-A", "PRESS-02", DateTimeOffset.Parse("2026-07-20T07:00:00Z"), rows.Count + 1);
        AddProcessExecution(rows, "OTHER", "LENS-B", "PRESS-01", DateTimeOffset.Parse("2026-07-20T06:00:00Z"), rows.Count + 1);
        var baselineVisual = Inspection("BASE", "WP-BASE", "optical.appearance.machine", withAttachment: true);
        var historyManual = Inspection("HISTORY", "WP-HISTORY", "optical.final.manual", withAttachment: false)
            with { Outcome = "FAIL" };
        var review = new InspectionReview
        {
            ReviewId = Guid.CreateVersion7(),
            InspectionRecordId = baselineVisual.RecordId,
            ExecutionId = "BASE",
            Decision = InspectionReviewDecisions.Confirmed,
            ReviewedAt = DateTimeOffset.UtcNow,
            ReviewedBy = "reviewer"
        };
        var reviewStore = new FakeReviewStore(new Dictionary<Guid, InspectionReview> { [baselineVisual.RecordId] = review });
        var service = new ExecutionComparisonService(
            new FakeEventStore(rows),
            new FakeInspectionStore([baselineVisual, historyManual]),
            reviewStore,
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.CompareWithHistoryAsync("BASE", 10);

        Assert.NotNull(result);
        Assert.Equal("LENS-A", result.ProductFamilyCode);
        Assert.Equal(600, result.Baseline.SampleCount);
        Assert.Equal(ProcessDataStatuses.Available, result.Baseline.ProcessDataQuality.Status);
        Assert.Equal(5, result.Baseline.PhaseCount);
        Assert.Equal(InspectionReviewDecisions.Confirmed, result.Baseline.VisualReviewDecision);
        Assert.Single(result.HistoricalProcessExecutions);
        Assert.Equal("HISTORY", result.HistoricalProcessExecutions[0].ExecutionId);
        Assert.Equal(600, result.Baseline.Signals.Single(item => item.Code == "press.load").SampleCount);
        Assert.Contains(result.QualityAssociations, item =>
            item.SignalCode == "press.load" &&
            item.PhaseCode == "30" &&
            item.PassProcessExecutionCount == 1 &&
            item.FailProcessExecutionCount == 1);
        Assert.Equal(2, result.Acceptance.CompleteProcessExecutionCount);

        var selected = await service.CompareSelectedAsync("HISTORY", ["BASE", "HISTORY"]);
        Assert.NotNull(selected);
        Assert.Equal("HISTORY", selected.BaselineProcessExecutionId);
        Assert.Equal("HISTORY", selected.Baseline.ExecutionId);
        Assert.Equal("BASE", Assert.Single(selected.HistoricalProcessExecutions).ExecutionId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompareSelectedAsync("BASE", ["BASE", "OTHER"]));
    }

    [Fact]
    public async Task ProcessExecutionUsesActuallyAppliedControlParametersBeforeProcessSpecificationMasterData()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z");
        var applied = Event(
            "process.specification.applied",
            "OPTIMIZED-RUN-01",
            "WP-OPTIMIZED-RUN-01",
            "LENS-A",
            startedAt,
            data: new Dictionary<string, object?>
            {
                ["resolvedParameters"] = new Dictionary<string, object?>
                {
                    ["processSpecification.upper_heat_compensation"] = 2.888943d
                }
            });
        var rows = new[]
        {
            Row(1, Event(
                "process.execution.started",
                "OPTIMIZED-RUN-01",
                "WP-OPTIMIZED-RUN-01",
                "LENS-A",
                startedAt)),
            Row(2, applied),
            Row(3, Event(
                "process.execution.completed",
                "OPTIMIZED-RUN-01",
                "WP-OPTIMIZED-RUN-01",
                "LENS-A",
                startedAt.AddMinutes(1)))
        };
        var service = new ExecutionComparisonService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var execution = await service.GetProcessExecutionAsync("OPTIMIZED-RUN-01");

        Assert.NotNull(execution);
        var parameter = Assert.Single(execution.ControlParameters);
        Assert.Equal("processSpecification.upper_heat_compensation", parameter.Code);
        Assert.Equal(2.888943d, parameter.Value.GetDouble());
    }

    [Fact]
    public async Task ProcessExecutionWithoutAppliedProcessSpecificationEvent_DoesNotExposePlannedProcessSpecificationAsActual()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z");
        var rows = new[]
        {
            Row(1, Event(
                "process.execution.started",
                "NO-ACTUAL-PARAMETERS",
                "WP-NO-ACTUAL-PARAMETERS",
                "LENS-A",
                startedAt)),
            Row(2, Event(
                "process.execution.completed",
                "NO-ACTUAL-PARAMETERS",
                "WP-NO-ACTUAL-PARAMETERS",
                "LENS-A",
                startedAt.AddMinutes(1)))
        };
        var plannedProcessSpecification = new ProcessSpecification
        {
            ProcessSpecificationId = "RCP-LENS-A",
            Name = "Planned processSpecification",
            Status = ConfigurationStatuses.Published,
            DataModelId = "optical-lens-molding",
            Values =
            [
                new ControlParameterValue
                {
                    Code = "processSpecification.upper_heat_compensation",
                    Value = System.Text.Json.JsonSerializer.SerializeToElement(9.9d)
                }
            ]
        };
        var service = new ExecutionComparisonService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore(plannedProcessSpecification)));

        var execution = await service.GetProcessExecutionAsync("NO-ACTUAL-PARAMETERS");

        Assert.NotNull(execution);
        Assert.Empty(execution.ControlParameters);
    }

    [Fact]
    public async Task ProcessExecutionsKeepAllSamplesAndUseConfiguredPhaseAndQualityRules()
    {
        var rows = new List<PlatformProductionEvent>();
        AddProcessExecution(rows, "CYCLE-RECORD", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        var service = new ProcessExecutionService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.QueryAsync(
            null, null, "LENS-A", null, null, null, null, null, "completed", 100);

        var execution = Assert.Single(result.Data);
        Assert.Equal(600, execution.SampleCount);
        Assert.Equal(ProcessDataStatuses.Available, execution.ProcessDataQuality.Status);
        Assert.True(execution.HasStarted);
        Assert.True(execution.HasCompleted);
        Assert.True(execution.LifecycleComplete);
        Assert.Equal(5, execution.Phases.Count);
        Assert.Equal("PENDING", execution.QualityStatus);
        Assert.Equal(2, execution.RequiredInspectionCount);
        Assert.Empty(execution.DataIssues);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Overview.SampleCompleteCount);
    }

    [Fact]
    public async Task ProcessExecutionRequiresBothStartAndEndEventsForLifecycleCompleteness()
    {
        var executionId = "MISSING-START";
        var completedAt = DateTimeOffset.Parse("2026-07-20T08:10:00Z");
        var rows = new[]
        {
            Row(1, Event(
                "process.execution.completed",
                executionId,
                "WP-MISSING-START",
                "LENS-A",
                completedAt,
                "PRESS-01"))
        };
        var service = new ProcessExecutionService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.QueryAsync(
            null, null, null, null, null, null, null, executionId, null, 100);

        var execution = Assert.Single(result.Data);
        Assert.False(execution.HasStarted);
        Assert.True(execution.HasCompleted);
        Assert.False(execution.LifecycleComplete);
        Assert.Equal("incomplete", execution.Status);
        Assert.Null(execution.DurationMs);
        Assert.Contains(execution.DataIssues, issue => issue.Code == "execution.start.missing");
        Assert.Equal(1, result.Overview.IncompleteCount);
        Assert.Equal(0, result.Overview.CompletedCount);
    }

    [Fact]
    public async Task ProcessExecutionPaginationReturnsFilteredTotalBeyondCurrentPage()
    {
        var rows = new List<PlatformProductionEvent>();
        AddProcessExecution(rows, "PAGE-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddProcessExecution(rows, "PAGE-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T09:00:00Z"), 2);
        var service = new ProcessExecutionService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var result = await service.QueryAsync(
            null, null, "LENS-A", null, null, null, null, null, "completed", 1, 1);

        Assert.Single(result.Data);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Overview.ExecutionCount);
        Assert.Equal(2, result.Overview.CompletedCount);
    }

    [Fact]
    public async Task ProcessExecutionsLinkSameBatchAcrossEquipmentAndCanNarrowByEdge()
    {
        static PlatformProductionEvent TaggedRow(
            long id,
            string type,
            string executionId,
            string outputItemId,
            string equipmentId,
            string edgeId,
            string batch,
            DateTimeOffset at)
        {
            var productionEvent = Event(type, executionId, outputItemId, "LENS-A", at, equipmentId);
            productionEvent = productionEvent with
            {
                Context = new Dictionary<string, string>(productionEvent.Context, StringComparer.Ordinal)
                {
                    ["external_batch_ref"] = batch
                }
            };
            return Row(id, productionEvent) with { EdgeId = edgeId };
        }

        var at = DateTimeOffset.Parse("2026-07-20T08:00:00Z");
        var rows = new[]
        {
            TaggedRow(1, "process.execution.started", "RUN-A", "WP-SHARED", "PRESS-01", "EDGE-A", "BATCH-42", at),
            TaggedRow(2, "process.execution.completed", "RUN-A", "WP-SHARED", "PRESS-01", "EDGE-A", "BATCH-42", at.AddMinutes(1)),
            TaggedRow(3, "process.execution.started", "RUN-B", "WP-SHARED", "PRESS-02", "EDGE-B", "BATCH-42", at.AddMinutes(2)),
            TaggedRow(4, "process.execution.completed", "RUN-B", "WP-SHARED", "PRESS-02", "EDGE-B", "BATCH-42", at.AddMinutes(3)),
            TaggedRow(5, "process.execution.started", "RUN-C", "WP-OTHER", "PRESS-03", "EDGE-B", "BATCH-OTHER", at.AddMinutes(4)),
            TaggedRow(6, "process.execution.completed", "RUN-C", "WP-OTHER", "PRESS-03", "EDGE-B", "BATCH-OTHER", at.AddMinutes(5))
        };
        var service = new ProcessExecutionService(
            new FakeEventStore(rows),
            new FakeInspectionStore([]),
            new FakeReviewStore(),
            new FakeMasterDataStore(),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()));

        var batch = await service.QueryAsync(
            null, null, null, null, null, null, null, null, "completed", 100,
            externalBatchRef: "BATCH-42");

        Assert.Equal(2, batch.Total);
        Assert.Equal(["PRESS-02", "PRESS-01"], batch.Data.Select(static row => row.EquipmentId).ToArray());
        Assert.All(batch.Data, static row => Assert.Equal("BATCH-42", row.ExternalBatchRef));
        Assert.Equal(["EDGE-A", "EDGE-B"], batch.Data.SelectMany(static row => row.EdgeIds)
            .Order(StringComparer.Ordinal).ToArray());

        var edge = await service.QueryAsync(
            null, null, null, null, null, null, null, null, "completed", 100,
            externalBatchRef: "BATCH-42",
            edgeId: "EDGE-B");
        Assert.Equal("RUN-B", Assert.Single(edge.Data).ExecutionId);
    }

    [Fact]
    public async Task ContinuousOperatingRegionsCompareWithoutProcessExecutionCorrelationSemantics()
    {
        var rows = new List<PlatformProductionEvent>();
        AddProcessExecution(rows, "RUN-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        // A previous product may complete exactly when the selected window starts.
        // Window context must come from its process samples, not that boundary event.
        rows.Add(Row(rows.Count + 1, Event(
            "process.execution.completed", "PRIOR-B", "WP-PRIOR-B", "LENS-B",
            DateTimeOffset.Parse("2026-07-20T10:00:00Z"), "PRESS-01")));
        AddProcessExecution(rows, "RUN-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T10:00:00Z"), rows.Count + 1);
        var service = new TimeWindowComparisonService(
            new FakeEventStore(rows),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()),
            new FakeInspectionStore([]));

        var result = await service.CompareAsync(new TimeWindowComparisonRequest
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
    public async Task ContinuousOperatingRegionsIncludeQualityScopesAndMeasurements()
    {
        var rows = new List<PlatformProductionEvent>();
        AddProcessExecution(rows, "RUN-A", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T08:00:00Z"), 1);
        AddProcessExecution(rows, "RUN-B", "LENS-A", "PRESS-01", DateTimeOffset.Parse("2026-07-20T10:00:00Z"), rows.Count + 1);
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
                OutputItemId = "WP-A",
                SubjectType = "optical-molding-machine",
                SubjectId = "PRESS-01",
                ProductFamilyCode = "LENS-A",
                InspectionPlanId = "lens.quality",
                From = DateTimeOffset.Parse("2026-07-20T08:00:00Z"),
                To = DateTimeOffset.Parse("2026-07-20T08:10:01Z")
            }
        ];
        var service = new TimeWindowComparisonService(
            new FakeEventStore(rows),
            new ProcessAnalysisResolver(new FakeProcessConfigurationStore()),
            new FakeInspectionStore([quality], scopes));

        var result = await service.CompareAsync(new TimeWindowComparisonRequest
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

    private static void AddProcessExecution(
        ICollection<PlatformProductionEvent> rows,
        string executionId,
        string productFamilyCode,
        string equipmentId,
        DateTimeOffset start,
        int firstIngestId)
    {
        var ingestId = firstIngestId;
        rows.Add(Row(ingestId++, Event(
            "process.execution.started",
            executionId,
            $"WP-{executionId}",
            productFamilyCode,
            start,
            equipmentId,
            new Dictionary<string, object?> { ["expectedSampleCount"] = 600 })));
        for (var second = 0; second < 600; second++)
        {
            var phase = second switch { < 90 => "10", < 240 => "20", < 360 => "30", < 480 => "40", _ => "50" };
            var evt = Event(
                "process.sample",
                executionId,
                $"WP-{executionId}",
                productFamilyCode,
                start.AddSeconds(second),
                equipmentId,
                new Dictionary<string, object?>
                {
                    ["values"] = new Dictionary<string, object?>
                    {
                        ["upper_mold.ir_temperature"] = 600d + second / 100d,
                        ["lower_mold.ir_temperature"] = 595d + second / 100d,
                        ["press.load"] = 120d,
                        ["chamber.vacuum"] = 12d,
                        ["servo.position"] = 12.5d,
                        ["process.stage_number"] = long.Parse(phase)
                    }
                });
            rows.Add(Row(ingestId++, evt));
        }
        rows.Add(Row(ingestId, Event(
            "process.execution.completed",
            executionId,
            $"WP-{executionId}",
            productFamilyCode,
            start.AddMinutes(10),
            equipmentId)));
    }

    private static ProductionEvent Event(
        string type,
        string executionId,
        string outputItemId,
        string productFamilyCode,
        DateTimeOffset occurredAt,
        string equipmentId = "PRESS-01",
        IReadOnlyDictionary<string, object?>? data = null)
        => new()
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = type,
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            Source = "edge/EDGE-001/PLC-01/test",
            Subject = new ObjectRef("optical-molding-machine", equipmentId),
            ExecutionId = executionId,
            Seq = 1,
            Context = new Dictionary<string, string>
            {
                ["output_item_id"] = outputItemId,
                ["product_family_code"] = productFamilyCode,
                ["product_code"] = $"{productFamilyCode}-01",
                ["process_specification_id"] = $"RCP-{productFamilyCode}",
                ["process_specification_version"] = "1",
                ["data_model_id"] = "optical-molding",
                ["data_model_version"] = "1"
            },
            Data = data ?? new Dictionary<string, object?>()
        };

    private static PlatformProductionEvent Row(long ingestId, ProductionEvent evt)
        => new() { IngestId = ingestId, EdgeId = "EDGE-001", IngestedAt = evt.RecordedAt, Event = evt };

    private static InspectionRecord Inspection(string executionId, string outputItemId, string definitionCode, bool withAttachment)
        => new()
        {
            RecordId = Guid.CreateVersion7(),
            OutputItemId = outputItemId,
            ExecutionId = executionId,
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
            if (!string.IsNullOrWhiteSpace(query.EdgeId)) filtered = filtered.Where(item => item.EdgeId == query.EdgeId);
            if (!string.IsNullOrWhiteSpace(query.EventType)) filtered = filtered.Where(item => item.Event.EventType == query.EventType);
            if (!string.IsNullOrWhiteSpace(query.SubjectType)) filtered = filtered.Where(item => item.Event.Subject.Type == query.SubjectType);
            if (!string.IsNullOrWhiteSpace(query.SubjectId)) filtered = filtered.Where(item => item.Event.Subject.Id == query.SubjectId);
            if (!string.IsNullOrWhiteSpace(query.ExecutionId)) filtered = filtered.Where(item => item.Event.ExecutionId == query.ExecutionId);
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
        public Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(IReadOnlyCollection<string> executionIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(records.Where(item => executionIds.Contains(item.ExecutionId)).ToArray());
    }

    private sealed class FakeReviewStore(IReadOnlyDictionary<Guid, InspectionReview>? latest = null) : IInspectionReviewStore
    {
        private readonly IReadOnlyDictionary<Guid, InspectionReview> _latest = latest ?? new Dictionary<Guid, InspectionReview>();
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string executionId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => Task.FromResult(_latest.Values.FirstOrDefault(item => item.ReviewId == reviewId));
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? executionId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionReview>>(_latest.Values.ToArray());
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
            Scope = new InspectionPlanScope { ProductFamilyCode = "lens-a" },
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
                    MappingId = $"test-{item.Item1}", ProcessSpecificationId = "RCP-LENS-A", ProcessStep = item.Item1, PhaseCode = item.Item2
                }).ToArray());
        public Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default) => Task.FromResult<PhaseMapping?>(null);
        public Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeatureDefinition>>(
            [new() { Code = "comparison.press.load", Name = "压力", PhaseCode = "execution", Signal = "press.load", Aggregation = "mean", Unit = "kg", Enabled = true, UseInComparison = true }]);
        public Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<FeatureDefinition?>(null);
        public Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProcessConfigurationStore(ProcessSpecification? processSpecification = null) : IProcessConfigurationStore
    {
        private static readonly ProcessDataModel Model = new()
        {
            ModelId = "optical-molding",
            Version = 1,
            Name = "光学模压",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new() { Code = "upper_mold.ir_temperature", DisplayName = "上模温度", Unit = "Cel" },
                    new() { Code = "lower_mold.ir_temperature", DisplayName = "下模温度", Unit = "Cel" },
                    new() { Code = "press.load", DisplayName = "压力", Unit = "kg" },
                    new() { Code = "chamber.vacuum", DisplayName = "真空度", Unit = "kPa" },
                    new() { Code = "servo.position", DisplayName = "伺服位置", Unit = "mm" },
                    new() { Code = "process.stage_number", DisplayName = "阶段号", DataType = "integer", Category = "stage", Nullable = false }
                ]
            }
        };

        private static readonly ProcessAnalysisPlan Plan = new()
        {
            PlanId = "execution-comparison",
            Version = 1,
            Name = "过程执行对比",
            Status = ConfigurationStatuses.Published,
            DataModelId = Model.ModelId,
            DataModelVersion = Model.Version,
            ComparisonKeys = ["product_family_code"],
            Signals = Model.Acquisition.DataItems.Where(static item => item.Category != "stage").Select(static item =>
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
        public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessSpecification>>([]);
        public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => Task.FromResult(processSpecification);
        public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([Plan, WindowPlan]);
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<ProcessAnalysisPlan?>(Plan);
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
