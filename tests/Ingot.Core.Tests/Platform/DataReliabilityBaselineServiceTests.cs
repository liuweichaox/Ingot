// 验证平台组件 DataReliabilityBaselineService 的成功、拒绝和安全边界。

using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
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
        var sourceEvidence = new ProcessSampleConfigurationEvidence
        {
            AppliedConfigurationKind = "ingestion-task",
            AppliedConfigurationId = "TASK-01",
            AppliedConfigurationVersion = 3,
            SampleFrameCount = 4,
            ObservedValueCount = 8,
            GoodValueCount = 7,
            BadValueCount = 1
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
            },
            SampleConfigurationEvidence = [sourceEvidence]
        };
        var service = new DataReliabilityBaselineService(
            new FakeEventStore(completed),
            new FakeProcessExecutionService(new Dictionary<string, ExecutionComparisonRow>
            {
                [good.ExecutionId] = good,
                [bad.ExecutionId] = bad
            }),
            new IdentitySampleConfigurationReferenceVerifier());

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001"
        });

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
        var impactedRun = Assert.Single(baseline.RunImpacts);
        Assert.Equal("RUN-BAD", impactedRun.ExecutionId);
        Assert.Equal(sourceEvidence, Assert.Single(impactedRun.SampleConfigurationEvidence));
        Assert.Contains(
            "不使用配方版本计划值",
            Assert.Single(baseline.Rates, item => item.Code == "actual_parameter_coverage").Definition);
    }

    [Fact]
    public async Task CalculateAsync_ShouldAttachVerifiedSourceEvidenceToRunImpacts()
    {
        var at = DateTimeOffset.UtcNow;
        var sourceEvidence = new ProcessSampleConfigurationEvidence
        {
            AppliedConfigurationKind = "ingestion-task",
            AppliedConfigurationId = "task-01",
            AppliedConfigurationVersion = 3,
            SourceEdgeId = "EDGE-01",
            SampleFrameCount = 2
        };
        var row = new ExecutionComparisonRow
        {
            ExecutionId = "RUN-01",
            EquipmentId = "PRESS-01",
            ProductFamilyCode = "lens",
            HasStarted = true,
            HasCompleted = false,
            LifecycleComplete = false,
            StartedAt = at.AddMinutes(-1),
            Context = new Dictionary<string, string>(),
            ProcessDataQuality = new ProcessDataQualitySummary { Status = ProcessDataStatuses.Degraded },
            SampleConfigurationEvidence = [sourceEvidence]
        };
        var verifier = new MarkingSampleConfigurationReferenceVerifier();
        var service = new DataReliabilityBaselineService(
            new FakeEventStore([Completed(1, "RUN-01", at)]),
            new FakeProcessExecutionService(new Dictionary<string, ExecutionComparisonRow> { [row.ExecutionId] = row }),
            verifier);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery { SiteId = "SITE-001" });

        Assert.Equal("SITE-001", verifier.RequestedSiteId);
        Assert.Equal(new[] { sourceEvidence }, verifier.ReceivedEvidence);
        var impact = Assert.Single(baseline.RunImpacts);
        var verified = Assert.Single(impact.SampleConfigurationEvidence);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Matched, verified.ReferenceVerificationStatus);
        Assert.Equal("published", verified.ConfigurationStatus);
    }

    [Fact]
    public async Task CalculateAsync_ShouldCompareAppliedVersionCoverageUsingPlatformReceiptTimes()
    {
        var now = DateTimeOffset.UtcNow;
        var startReceivedAt = now.AddMinutes(-8);
        var completedReceivedAt = now.AddMinutes(-4);
        var deviceStartedAt = now.AddDays(-20);
        var deviceCompletedAt = deviceStartedAt.AddMinutes(10);
        var sample = ReceiptTimeSampleEvidence(
            now.AddMinutes(-6), now.AddMinutes(-5.5), deviceStartedAt.AddMinutes(5));
        var history = new RecordingDeploymentHistory([ReceiptTimeObservation(now)]);
        var eventStore = new FakeEventStore(
        [
            Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, startReceivedAt),
            Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceCompletedAt, completedReceivedAt)
        ]);
        var service = ReceiptTimeCoverageService(
            [
                Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, startReceivedAt),
                Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceCompletedAt, completedReceivedAt)
            ],
            sample,
            history,
            eventStore);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001"
        });

        var run = Assert.Single(baseline.RunImpacts);
        var coverage = Assert.Single(run.ExpectedConfigurationCoverage);
        Assert.Equal(startReceivedAt, run.StartedPlatformIngestedAt);
        Assert.Equal(completedReceivedAt, run.CompletedPlatformIngestedAt);
        Assert.Equal(startReceivedAt, history.From);
        Assert.Equal(completedReceivedAt, history.To);
        Assert.Equal(ExpectedConfigurationSampleEvidenceStatuses.Matched, coverage.SampleEvidenceStatus);
        Assert.Equal(new[] { "temperature.actual" }, coverage.RequiredDataItemCodes);
        Assert.Equal(new[] { "temperature.actual" }, coverage.ObservedDataItemCodes);
        Assert.Empty(coverage.NotObservedRequiredDataItemCodes);
        Assert.Equal(now.AddMinutes(-7), coverage.RunOverlapStart);
        Assert.Equal(now.AddMinutes(-4.5), coverage.RunOverlapEnd);
        Assert.Equal(2, eventStore.Queries.Count);
        Assert.DoesNotContain(eventStore.Queries, static query => query.ExecutionIds is not null);
    }

    [Fact]
    public async Task CalculateAsync_ShouldRecoverRunStartReceiptBeforeTheSelectedEventTimeRange()
    {
        var now = DateTimeOffset.UtcNow;
        var deviceStartedAt = now.AddMinutes(-20);
        var deviceCompletedAt = now.AddMinutes(-5);
        var startReceivedAt = now.AddMinutes(-18);
        var completedReceivedAt = now.AddMinutes(-4);
        var history = new RecordingDeploymentHistory([ReceiptTimeObservation(now)]);
        var eventStore = new FakeEventStore(
        [
            Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, startReceivedAt),
            Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceCompletedAt, completedReceivedAt)
        ]);
        var service = ReceiptTimeCoverageService(
            [
                Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, startReceivedAt),
                Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceCompletedAt, completedReceivedAt)
            ],
            ReceiptTimeSampleEvidence(
                now.AddMinutes(-6), now.AddMinutes(-5.5), deviceStartedAt.AddMinutes(15)),
            history,
            eventStore);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = now.AddMinutes(-8),
            To = now
        });

        var run = Assert.Single(baseline.RunImpacts);
        var coverage = Assert.Single(run.ExpectedConfigurationCoverage);
        Assert.Equal(startReceivedAt, run.StartedPlatformIngestedAt);
        Assert.Equal(completedReceivedAt, run.CompletedPlatformIngestedAt);
        Assert.Equal(startReceivedAt, history.From);
        Assert.Equal(completedReceivedAt, history.To);
        Assert.Equal(ExpectedConfigurationSampleEvidenceStatuses.Matched, coverage.SampleEvidenceStatus);
        var recoveryQueries = eventStore.Queries.Where(static query => query.ExecutionIds is not null).ToArray();
        Assert.Equal(2, recoveryQueries.Length);
        Assert.All(recoveryQueries, query =>
        {
            Assert.Equal(new[] { "RUN-RECEIPT" }, query.ExecutionIds);
            Assert.Null(query.From);
            Assert.Null(query.To);
        });
        Assert.Equal(
            new[] { "process.execution.completed", "process.execution.started" },
            recoveryQueries
                .Select(static query => query.EventType)
                .OrderBy(static type => type, StringComparer.Ordinal));
    }

    [Fact]
    public async Task CalculateAsync_ShouldRecoverEarliestAndLatestBoundaryWhenRangeContainsPartialDuplicates()
    {
        var now = DateTimeOffset.UtcNow;
        var queryFrom = now.AddMinutes(-40);
        var reportEnd = now.AddMinutes(-10);
        var earliestStartReceipt = now.AddMinutes(-58);
        var inRangeStartReceipt = now.AddMinutes(-29);
        var inRangeCompletionReceipt = now.AddMinutes(-18);
        var latestCompletionReceipt = now.AddMinutes(-12);
        PlatformProductionEvent[] lifecycleEvents =
        [
            Lifecycle("process.execution.started", 1, "RUN-RECEIPT", now.AddMinutes(-60), earliestStartReceipt),
            Lifecycle("process.execution.started", 2, "RUN-RECEIPT", now.AddMinutes(-30), inRangeStartReceipt),
            Lifecycle("process.execution.completed", 3, "RUN-RECEIPT", now.AddMinutes(-20), inRangeCompletionReceipt),
            Lifecycle("process.execution.completed", 4, "RUN-RECEIPT", now.AddMinutes(-5), latestCompletionReceipt)
        ];
        var eventStore = new FakeEventStore(lifecycleEvents);
        var history = new RecordingDeploymentHistory([
            new EdgeTaskDeploymentObservation
            {
                EdgeId = "EDGE-01",
                TaskId = "TASK-01",
                TaskVersion = 3,
                ConfigurationHash = "hash-3",
                ConfigurationSource = "Platform",
                FirstObservedAt = now.AddMinutes(-22),
                LastObservedAt = now.AddMinutes(-15),
                ClosedObservedAt = now.AddMinutes(-14)
            }
        ]);
        var service = ReceiptTimeCoverageService(
            lifecycleEvents,
            ReceiptTimeSampleEvidence(
                now.AddMinutes(-20), now.AddMinutes(-18.5), now.AddMinutes(-25)),
            history,
            eventStore);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = queryFrom,
            To = reportEnd
        });

        var run = Assert.Single(baseline.RunImpacts);
        Assert.Equal(earliestStartReceipt, run.StartedPlatformIngestedAt);
        Assert.Equal(latestCompletionReceipt, run.CompletedPlatformIngestedAt);
        Assert.Equal(earliestStartReceipt, history.From);
        Assert.Equal(latestCompletionReceipt, history.To);
        var coverage = Assert.Single(run.ExpectedConfigurationCoverage);
        Assert.Equal(ExpectedConfigurationSampleEvidenceStatuses.Matched, coverage.SampleEvidenceStatus);
        var recoveryQueries = eventStore.Queries.Where(static query => query.ExecutionIds is not null).ToArray();
        Assert.Equal(2, recoveryQueries.Length);
        Assert.All(recoveryQueries, query =>
        {
            Assert.Equal(new[] { "RUN-RECEIPT" }, query.ExecutionIds);
            Assert.Null(query.From);
            Assert.Null(query.To);
        });
    }

    [Fact]
    public async Task CalculateAsync_ShouldKeepSampleCoverageUnknownWhenReceiptRangeCrossesObservation()
    {
        var now = DateTimeOffset.UtcNow;
        var deviceStartedAt = now.AddDays(-20);
        var service = ReceiptTimeCoverageService(
            [
                Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt,
                    now.AddMinutes(-8)),
                Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceStartedAt.AddMinutes(10),
                    now.AddMinutes(-4))
            ],
            ReceiptTimeSampleEvidence(now.AddMinutes(-6), now.AddMinutes(-4), deviceStartedAt.AddMinutes(5)),
            new RecordingDeploymentHistory([ReceiptTimeObservation(now)]));

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = now.AddDays(-30),
            To = now
        });

        var coverage = Assert.Single(Assert.Single(baseline.RunImpacts).ExpectedConfigurationCoverage);
        Assert.Equal(
            ExpectedConfigurationSampleEvidenceStatuses.UnverifiableSampleTimeRange,
            coverage.SampleEvidenceStatus);
        Assert.Empty(coverage.RequiredDataItemCodes);
        Assert.Empty(coverage.ObservedDataItemCodes);
        Assert.Empty(coverage.NotObservedRequiredDataItemCodes);
    }

    [Fact]
    public async Task CalculateAsync_ShouldBoundOpenRunCoverageToTheReportEnd()
    {
        var now = DateTimeOffset.UtcNow;
        var reportEnd = now.AddMinutes(-10);
        var deviceStartedAt = now.AddDays(-20);
        var history = new RecordingDeploymentHistory([
            new EdgeTaskDeploymentObservation
            {
                EdgeId = "EDGE-01",
                TaskId = "TASK-01",
                TaskVersion = 3,
                ConfigurationHash = "hash-3",
                ConfigurationSource = "Platform",
                FirstObservedAt = reportEnd.AddMinutes(1),
                LastObservedAt = reportEnd.AddMinutes(4)
            }
        ]);
        var service = ReceiptTimeCoverageService(
            [Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, reportEnd.AddMinutes(-5))],
            ReceiptTimeSampleEvidence(
                reportEnd.AddMinutes(1), reportEnd.AddMinutes(2), deviceStartedAt.AddMinutes(5)),
            history);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = now.AddDays(-30),
            To = reportEnd
        });

        var run = Assert.Single(baseline.RunImpacts);
        Assert.False(run.HasCompleted);
        Assert.Empty(run.ExpectedConfigurationCoverage);
        Assert.Equal(reportEnd, history.To);
    }

    [Fact]
    public async Task CalculateAsync_ShouldBoundCompletedRunCoverageToTheReportEnd()
    {
        var now = DateTimeOffset.UtcNow;
        var reportEnd = now.AddMinutes(-10);
        var deviceStartedAt = now.AddDays(-20);
        var history = new RecordingDeploymentHistory([
            new EdgeTaskDeploymentObservation
            {
                EdgeId = "EDGE-01",
                TaskId = "TASK-01",
                TaskVersion = 3,
                ConfigurationHash = "hash-3",
                ConfigurationSource = "Platform",
                FirstObservedAt = reportEnd.AddMinutes(1),
                LastObservedAt = reportEnd.AddMinutes(4)
            }
        ]);
        var service = ReceiptTimeCoverageService(
            [
                Lifecycle("process.execution.started", 1, "RUN-RECEIPT", deviceStartedAt, reportEnd.AddMinutes(-5)),
                Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceStartedAt.AddMinutes(10), reportEnd.AddMinutes(5))
            ],
            ReceiptTimeSampleEvidence(
                reportEnd.AddMinutes(1), reportEnd.AddMinutes(2), deviceStartedAt.AddMinutes(5)),
            history);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = now.AddDays(-30),
            To = reportEnd
        });

        var run = Assert.Single(baseline.RunImpacts);
        Assert.True(run.HasCompleted);
        Assert.Empty(run.ExpectedConfigurationCoverage);
        Assert.Equal(reportEnd, history.To);
    }

    [Fact]
    public async Task CalculateAsync_ShouldNotInferCoverageWhenLifecycleReceiptBoundaryIsMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var deviceStartedAt = now.AddDays(-20);
        var history = new RecordingDeploymentHistory([ReceiptTimeObservation(now)]);
        var service = ReceiptTimeCoverageService(
            [
                Lifecycle("process.execution.completed", 2, "RUN-RECEIPT", deviceStartedAt.AddMinutes(10),
                    now.AddMinutes(-4))
            ],
            ReceiptTimeSampleEvidence(now.AddMinutes(-6), now.AddMinutes(-5), deviceStartedAt.AddMinutes(5)),
            history);

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            From = now.AddDays(-30),
            To = now
        });

        var run = Assert.Single(baseline.RunImpacts);
        Assert.Null(run.StartedPlatformIngestedAt);
        Assert.Empty(run.ExpectedConfigurationCoverage);
        Assert.Equal(0, history.CallCount);
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
            new FakeProcessExecutionService(rows.ToDictionary(static row => row.ExecutionId)),
            new IdentitySampleConfigurationReferenceVerifier());

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001"
        });

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

    [Fact]
    public async Task CalculateAsync_EquipmentFilterIncludesCavitiesAndLegacyEquipmentButNotOtherSubjects()
    {
        var now = DateTimeOffset.UtcNow;
        var completed = new[]
        {
            Completed(1, "RUN-CAVITY-1", now.AddMinutes(-5), "cavity", "PRESS-01:CAVITY-1", "PRESS-01"),
            Completed(2, "RUN-CAVITY-2", now.AddMinutes(-4), "cavity", "PRESS-01:CAVITY-2", "PRESS-01"),
            Completed(3, "RUN-LEGACY", now.AddMinutes(-3), "equipment", "PRESS-01", null),
            Completed(4, "RUN-NON-EQUIPMENT", now.AddMinutes(-2), "cavity", "PRESS-01", null),
            Completed(5, "RUN-OTHER-MACHINE", now.AddMinutes(-1), "cavity", "PRESS-02:CAVITY-1", "PRESS-02")
        };
        var service = new DataReliabilityBaselineService(
            new FakeEventStore(completed),
            new FakeProcessExecutionService(new Dictionary<string, ExecutionComparisonRow>()),
            new IdentitySampleConfigurationReferenceVerifier());

        var baseline = await service.CalculateAsync(new DataReliabilityBaselineQuery
        {
            SiteId = "SITE-001",
            EquipmentId = "PRESS-01"
        });

        Assert.Equal(3, baseline.MatchingCompletedRunCount);
        Assert.Equal(0, baseline.AnalyzedRunCount);
    }

    private static double? Rate(DataReliabilityBaseline value, string code)
        => Assert.Single(value.Rates, item => item.Code == code).Rate;

    private static PlatformProductionEvent Completed(
        long ingestId,
        string executionId,
        DateTimeOffset at,
        string subjectType = "equipment",
        string subjectId = "PRESS-01",
        string? equipmentId = "PRESS-01")
        => Lifecycle("process.execution.completed", ingestId, executionId, at, at,
            subjectType, subjectId, equipmentId);

    private static PlatformProductionEvent Lifecycle(
        string eventType,
        long ingestId,
        string executionId,
        DateTimeOffset occurredAt,
        DateTimeOffset ingestedAt,
        string subjectType = "equipment",
        string subjectId = "PRESS-01",
        string? equipmentId = "PRESS-01")
        => new()
        {
            IngestId = ingestId,
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            IngestedAt = ingestedAt,
            Event = ProductionEvent.Create(
                eventType,
                occurredAt,
                "edge/EDGE-001/equipment/PRESS-01",
                new ObjectRef(subjectType, subjectId),
                executionId,
                equipmentId is null ? null : new Dictionary<string, string> { ["equipment_id"] = equipmentId }) with
            {
                Seq = ingestId
            }
        };

    private static ProcessSampleConfigurationEvidence ReceiptTimeSampleEvidence(
        DateTimeOffset firstIngestedAt,
        DateTimeOffset lastIngestedAt,
        DateTimeOffset deviceOccurredAt)
        => new()
        {
            AppliedConfigurationKind = "ingestion-task",
            AppliedConfigurationId = "TASK-01",
            AppliedConfigurationVersion = 3,
            SourceEdgeId = "EDGE-01",
            SampleFrameCount = 2,
            FirstOccurredAt = deviceOccurredAt,
            LastOccurredAt = deviceOccurredAt.AddSeconds(30),
            FirstPlatformIngestedAt = firstIngestedAt,
            LastPlatformIngestedAt = lastIngestedAt,
            HasCompletePlatformIngestedAt = true,
            ObservedDataItemCodes = ["temperature.actual"]
        };

    private static EdgeTaskDeploymentObservation ReceiptTimeObservation(DateTimeOffset now)
        => new()
        {
            EdgeId = "EDGE-01",
            TaskId = "TASK-01",
            TaskVersion = 3,
            ConfigurationHash = "hash-3",
            ConfigurationSource = "Platform",
            FirstObservedAt = now.AddMinutes(-7),
            LastObservedAt = now.AddMinutes(-5),
            ClosedObservedAt = now.AddMinutes(-4.5)
        };

    private static DataReliabilityBaselineService ReceiptTimeCoverageService(
        IReadOnlyList<PlatformProductionEvent> lifecycleEvents,
        ProcessSampleConfigurationEvidence sample,
        RecordingDeploymentHistory history,
        FakeEventStore? eventStore = null)
    {
        var row = new ExecutionComparisonRow
        {
            ExecutionId = "RUN-RECEIPT",
            EquipmentId = "PRESS-01",
            SubjectType = "equipment",
            SubjectId = "PRESS-01",
            EdgeIds = ["EDGE-01"],
            ProductFamilyCode = "lens",
            StartedAt = lifecycleEvents.FirstOrDefault(static item =>
                item.Event.EventType == "process.execution.started")?.Event.OccurredAt ?? DateTimeOffset.UnixEpoch,
            CompletedAt = lifecycleEvents.FirstOrDefault(static item =>
                item.Event.EventType == "process.execution.completed")?.Event.OccurredAt,
            HasStarted = true,
            HasCompleted = lifecycleEvents.Any(static item =>
                item.Event.EventType == "process.execution.completed"),
            LifecycleComplete = lifecycleEvents.Any(static item =>
                item.Event.EventType == "process.execution.completed"),
            Context = new Dictionary<string, string>(),
            ProcessDataQuality = new ProcessDataQualitySummary { Status = ProcessDataStatuses.Degraded },
            SampleConfigurationEvidence = [sample]
        };
        return new DataReliabilityBaselineService(
            eventStore ?? new FakeEventStore(lifecycleEvents),
            new FakeProcessExecutionService(new Dictionary<string, ExecutionComparisonRow>
            {
                [row.ExecutionId] = row
            }),
            new MarkingSampleConfigurationReferenceVerifier(),
            taskDeploymentHistory: history,
            edgeSites: new FixedEdgeSiteDirectory(new HashSet<string>(["EDGE-01"], StringComparer.OrdinalIgnoreCase)),
            ingestionTasks: new FixedCoverageTaskStore([
                new IngestionTaskCoverageMetadata(
                    "TASK-01", 3, "EDGE-01", "published", "equipment", "PRESS-01",
                    ["temperature.actual"], false, false)
            ]));
    }

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
        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(string executionId, CancellationToken ct = default, string? siteId = null)
            => Task.FromResult(rows.GetValueOrDefault(executionId));

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                executionIds.Where(rows.ContainsKey)
                    .ToDictionary(id => id, id => rows[id], StringComparer.Ordinal));

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => throw new NotSupportedException();

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => throw new NotSupportedException();
    }

    private sealed class IdentitySampleConfigurationReferenceVerifier
        : IProcessSampleConfigurationReferenceVerifier
    {
        public Task<IReadOnlyList<ProcessSampleConfigurationEvidence>> VerifyAsync(
            string siteId,
            IReadOnlyList<ProcessSampleConfigurationEvidence> evidence,
            CancellationToken ct = default)
            => Task.FromResult(evidence);
    }

    private sealed class MarkingSampleConfigurationReferenceVerifier
        : IProcessSampleConfigurationReferenceVerifier
    {
        public string? RequestedSiteId { get; private set; }
        public IReadOnlyList<ProcessSampleConfigurationEvidence> ReceivedEvidence { get; private set; } = [];

        public Task<IReadOnlyList<ProcessSampleConfigurationEvidence>> VerifyAsync(
            string siteId,
            IReadOnlyList<ProcessSampleConfigurationEvidence> evidence,
            CancellationToken ct = default)
        {
            RequestedSiteId = siteId;
            ReceivedEvidence = evidence;
            return Task.FromResult<IReadOnlyList<ProcessSampleConfigurationEvidence>>(evidence
                .Select(static item => item with
                {
                    ReferenceVerificationStatus = ProcessSampleConfigurationReferenceStatuses.Matched,
                    ConfigurationStatus = "published"
                })
                .ToArray());
        }
    }

    private sealed class FakeEventStore(
        IReadOnlyList<PlatformProductionEvent> rows) : IPlatformEventStore
    {
        public List<PlatformEventQuery> Queries { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(rows
                .Where(row => string.IsNullOrWhiteSpace(query.SiteId) || row.SiteId == query.SiteId)
                .Where(row => query.EventType is null || row.Event.EventType == query.EventType)
                .Where(row => query.ExecutionIds is null || query.ExecutionIds.Contains(
                    row.Event.ExecutionId ?? "", StringComparer.Ordinal))
                .Where(row => !query.From.HasValue || row.Event.OccurredAt >= query.From.Value)
                .Where(row => !query.To.HasValue || row.Event.OccurredAt <= query.To.Value)
                .Where(row => string.IsNullOrWhiteSpace(query.EquipmentId) ||
                              PhysicalEquipmentIdentity.Resolve(row.Event) == query.EquipmentId.Trim())
                .Where(row => !query.AfterIngestId.HasValue || row.IngestId > query.AfterIngestId)
                .OrderBy(static row => row.IngestId)
                .Take(query.Limit)
                .ToArray());
        }

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryByExecutionIdsAsync(
            IReadOnlyCollection<string> executionIds,
            string siteId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(rows
                .Where(row => string.Equals(row.SiteId, siteId, StringComparison.Ordinal) &&
                              row.Event.ExecutionId is not null &&
                              executionIds.Contains(row.Event.ExecutionId))
                .ToArray());

        public Task<IReadOnlyList<PlatformProcessExecutionSummarySource>> QueryExecutionSummarySourcesAsync(
            IReadOnlyCollection<string> executionIds,
            string siteId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProcessExecutionSummarySource>>(rows
                .Where(row => string.Equals(row.SiteId, siteId, StringComparison.Ordinal) &&
                              row.Event.ExecutionId is not null &&
                              executionIds.Contains(row.Event.ExecutionId))
                .GroupBy(row => row.Event.ExecutionId!, StringComparer.Ordinal)
                .Select(group => new PlatformProcessExecutionSummarySource
                {
                    ExecutionId = group.Key,
                    Events = group.ToArray()
                })
                .ToArray());

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new PlatformEventScopeStats { Count = rows.Count });

        public Task<bool> CanConnectAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<DataObjectPage> QueryDataObjectsAsync(DataObjectQuery query, CancellationToken ct = default)
            => Task.FromResult(new DataObjectPage { Limit = query.Limit, Offset = query.Offset });
    }

    private sealed class FixedEdgeSiteDirectory(IReadOnlySet<string> edgeIds) : IEdgeSiteDirectory
    {
        public Task<IReadOnlySet<string>> ListEdgeIdsAsync(string siteId, CancellationToken ct = default)
            => Task.FromResult(edgeIds);
    }

    private sealed class RecordingDeploymentHistory(
        IReadOnlyList<EdgeTaskDeploymentObservation> observations) : IEdgeTaskDeploymentHistoryReader
    {
        public DateTimeOffset? From { get; private set; }
        public DateTimeOffset? To { get; private set; }
        public int CallCount { get; private set; }

        public Task<EdgeTaskDeploymentObservationQueryResult> FindOverlappingAsync(
            string siteId,
            IReadOnlyCollection<string> edgeIds,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken ct = default)
        {
            CallCount++;
            From = from;
            To = to;
            return Task.FromResult(new EdgeTaskDeploymentObservationQueryResult(observations, false));
        }
    }

    private sealed class FixedCoverageTaskStore(
        IReadOnlyList<IngestionTaskCoverageMetadata> coverageMetadata) : ILegacyIngestionMetadataReader
    {
        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTaskVersionMetadata>> FindVersionMetadataAsync(
            IReadOnlyCollection<IngestionTaskVersionReference> references,
            IReadOnlyCollection<string> authorizedEdgeIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTaskCoverageMetadata>> FindCoverageMetadataAsync(
            IReadOnlyCollection<IngestionTaskVersionReference> references,
            IReadOnlyCollection<string> authorizedEdgeIds,
            CancellationToken ct = default)
            => Task.FromResult(coverageMetadata);
        public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
