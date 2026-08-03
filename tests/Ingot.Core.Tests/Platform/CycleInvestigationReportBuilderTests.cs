using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class CycleInvestigationReportBuilderTests
{
    [Fact]
    public void Build_ProducesAuditableSectionsAndBlockedExperiment()
    {
        var target = Row("FAIL-1", "FAIL", "PRESS-B") with
        {
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available
            },
            AnalysisMaterialization = new CycleAnalysisMaterialization
            {
                SourceContentHash = new string('a', 64)
            }
        };
        var historical = new[] { Row("PASS-1", "PASS", "PRESS-A") };
        var candidate = new CycleCauseCandidate
        {
            CandidateId = "recipe:temperature",
            SourceKind = CycleCauseSourceKinds.RecipeParameter,
            Actionability = CycleCauseActionability.Controllable,
            VariableCode = "temperature",
            DataSource = "recipe:temperature",
            DisplayName = "温度",
            EvidenceLevel = "exploratory",
            CandidateScore = 0.8,
            PossibleConfounders = ["machine_id"]
        };
        var report = new CycleInvestigationReportBuilder().Build(
            target,
            historical,
            [new CycleSignalComparison
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
            new CycleDiagnosisSummary
            {
                EvidenceLevel = "exploratory",
                Candidates = [candidate]
            },
            new CycleComparisonAcceptance
            {
                CompleteCycleCount = 2,
                QualityLinkedCycleCount = 2,
                AvailableCycleCount = 2,
                EffectiveCycleWeight = 1
            },
            ["product_series"]);

        Assert.Equal("exploratory", report.Status);
        Assert.Equal("FAIL-1", report.TargetCycleId);
        Assert.Equal("lens-a", report.ComparisonBaseline.MatchingContext["product_series"]);
        Assert.Single(report.FirstDeviations);
        Assert.Single(report.CandidateCauses);
        Assert.Contains(report.CounterEvidence, value => value.Kind == "observational-only");
        Assert.Contains(report.CounterEvidence, value => value.Kind == "confounding");
        Assert.Contains("machine_id", report.Confounders);
        var experiment = Assert.Single(report.NextExperiments);
        Assert.Equal(2, experiment.MinimumLevels);
        Assert.Equal(2, experiment.MinimumBlocks);
        Assert.Equal(2, experiment.RepeatsPerCondition);
        Assert.Contains("machine_id", experiment.BlockingFactors);
    }

    [Fact]
    public void Build_RefusesReadyStatusWhenTargetDataIsUnavailable()
    {
        var target = Row("FAIL-1", "FAIL", "PRESS-A");
        var report = new CycleInvestigationReportBuilder().Build(
            target,
            [Row("PASS-1", "PASS", "PRESS-A")],
            [],
            new CycleDiagnosisSummary { EvidenceLevel = "stable" },
            new CycleComparisonAcceptance(),
            ["product_series"]);

        Assert.Equal("insufficient", report.Status);
        Assert.Contains(report.MissingData, item => item.Contains("偏离", StringComparison.Ordinal));
    }

    private static CycleComparisonRow Row(string id, string outcome, string machineId)
        => new()
        {
            CorrelationId = id,
            MachineId = machineId,
            Context = new Dictionary<string, string> { ["product_series"] = "lens-a" },
            LifecycleComplete = true,
            StartedAt = DateTimeOffset.Parse("2026-08-03T08:00:00Z"),
            ProductSeries = "lens-a",
            EvidenceWeight = 1,
            InspectionOutcomes = [outcome],
            RecipeParameters =
            [
                new CycleRecipeParameter
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
