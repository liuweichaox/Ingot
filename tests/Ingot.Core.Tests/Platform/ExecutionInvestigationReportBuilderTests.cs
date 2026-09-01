// 验证平台组件 ExecutionInvestigationReportBuilder 的成功、拒绝和安全边界。

using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ExecutionInvestigationReportBuilderTests
{
    [Fact]
    public void Build_ProducesAuditableSectionsAndBlockedEvidenceAction()
    {
        var target = Row("FAIL-1", "FAIL", "PRESS-B") with
        {
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available
            },
            AnalysisMaterialization = new ProcessExecutionAnalysisMaterialization
            {
                SourceContentHash = new string('a', 64)
            }
        };
        var historical = new[] { Row("PASS-1", "PASS", "PRESS-A") };
        var candidate = new ExecutionCauseCandidate
        {
            CandidateId = "control-parameter:temperature",
            SourceKind = ExecutionCauseSourceKinds.ProcessSpecificationParameter,
            Actionability = ExecutionCauseActionability.Controllable,
            VariableCode = "temperature",
            DataSource = "control-parameter:temperature",
            DisplayName = "温度",
            EvidenceLevel = "exploratory",
            CandidateScore = 0.8,
            PossibleConfounders = ["equipment_id"]
        };
        var report = new ExecutionInvestigationReportBuilder().Build(
            target,
            historical,
            [new ProcessSignalComparison
            {
                SignalCode = "temperature",
                FeatureCode = "mean",
                PhaseCode = "press",
                PhaseName = "压制",
                PhaseOrder = 1,
                BaselineValue = 515,
                HistoricalMedian = 500,
                RobustDeviation = 3.2
            }],
            new ExecutionDiagnosisSummary
            {
                EvidenceLevel = "exploratory",
                Candidates = [candidate]
            },
            new ExecutionComparisonAcceptance
            {
                CompleteProcessExecutionCount = 2,
                QualityLinkedProcessExecutionCount = 2,
                AvailableProcessExecutionCount = 2,
                EffectiveProcessExecutionWeight = 1
            },
            ["product_family_code"]);

        Assert.Equal("exploratory", report.Status);
        Assert.Equal("FAIL-1", report.TargetProcessExecutionId);
        Assert.Equal("lens-a", report.ComparisonBaseline.MatchingContext["product_family_code"]);
        Assert.Single(report.FirstDeviations);
        Assert.Single(report.CandidateCauses);
        Assert.Contains(report.CounterEvidence, value => value.Kind == "observational-only");
        Assert.Contains(report.CounterEvidence, value => value.Kind == "confounding");
        Assert.Contains("equipment_id", report.Confounders);
        var action = Assert.Single(report.NextEvidenceActions);
        Assert.Equal(2, action.MinimumLevels);
        Assert.Equal(2, action.MinimumBlocks);
        Assert.Equal(2, action.RepeatsPerCondition);
        Assert.Contains("equipment_id", action.BlockingFactors);
    }

    [Fact]
    public void Build_RefusesReadyStatusWhenTargetDataIsUnavailable()
    {
        var target = Row("FAIL-1", "FAIL", "PRESS-A");
        var report = new ExecutionInvestigationReportBuilder().Build(
            target,
            [Row("PASS-1", "PASS", "PRESS-A")],
            [],
            new ExecutionDiagnosisSummary { EvidenceLevel = "stable" },
            new ExecutionComparisonAcceptance(),
            ["product_family_code"]);

        Assert.Equal("insufficient", report.Status);
        Assert.Contains(report.MissingData, item => item.Contains("偏离", StringComparison.Ordinal));
    }

    private static ExecutionComparisonRow Row(string id, string outcome, string equipmentId)
        => new()
        {
            ExecutionId = id,
            EquipmentId = equipmentId,
            Context = new Dictionary<string, string> { ["product_family_code"] = "lens-a" },
            LifecycleComplete = true,
            StartedAt = DateTimeOffset.Parse("2026-08-03T08:00:00Z"),
            ProductFamilyCode = "lens-a",
            EvidenceWeight = 1,
            InspectionOutcomes = [outcome],
            ControlParameters =
            [
                new ExecutionControlParameterValue
                {
                    Code = "temperature",
                    Value = JsonSerializer.SerializeToElement(500)
                }
            ],
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Unavailable
            }
        };
}
