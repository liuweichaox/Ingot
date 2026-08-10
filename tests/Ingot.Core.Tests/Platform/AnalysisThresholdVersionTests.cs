using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AnalysisThresholdVersionTests
{
    [Fact]
    public void CycleAlgorithmVersion_ContainsFingerprintOfEveryDecisionThreshold()
    {
        var canonical = string.Join('|',
            CycleAnalysisThresholds.CandidateScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            CycleAnalysisThresholds.ModelRankScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            CycleAnalysisThresholds.HighStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            CycleAnalysisThresholds.ModerateStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            CycleAnalysisThresholds.MinimumSignalCoverage.ToString("R", CultureInfo.InvariantCulture));
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];

        Assert.Equal(expected, CycleAnalysisThresholds.ComputeFingerprint());
        Assert.Equal($"stage-relative-v5+{expected}", WholeCycleAnalysisEngine.AlgorithmVersion);
    }

    [Fact]
    public void ResearchValidationPolicy_ContainsFingerprintOfItsCalibrationGates()
    {
        var canonical = string.Join('|',
            ValidationThresholds.MinimumCalibrationCheckCount.ToString(CultureInfo.InvariantCulture),
            ValidationThresholds.MinimumCalibrationCoverage.ToString("R", CultureInfo.InvariantCulture));
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];

        Assert.Equal(expected, ValidationThresholds.ComputeFingerprint());
        Assert.Equal($"research-validation-v1+{expected}", ValidationThresholds.PolicyVersion);
    }

    [Fact]
    public void HistoricalPayloadsWithoutVersionFields_RetainLegacyMeaning()
    {
        var materialization = JsonSerializer.Deserialize<CycleAnalysisMaterialization>("{}");
        Assert.Equal("stage-relative-v1", materialization!.AlgorithmVersion);

        var comparison = new CycleComparisonResult
        {
            BaselineCycleId = "cycle-1",
            ProductSeries = "series-1",
            Baseline = new CycleComparisonRow
            {
                CorrelationId = "cycle-1",
                ProductSeries = "series-1",
                MachineId = "machine-1",
                StartedAt = DateTimeOffset.UnixEpoch
            },
            Acceptance = new CycleComparisonAcceptance()
        };
        var comparisonJson = JsonNode.Parse(JsonSerializer.Serialize(comparison))!.AsObject();
        comparisonJson.Remove(nameof(CycleComparisonResult.FeatureAlgorithmVersion));
        var restoredComparison = comparisonJson.Deserialize<CycleComparisonResult>();
        Assert.Equal("stage-relative-v1", restoredComparison!.FeatureAlgorithmVersion);

        var shadowJson = JsonNode.Parse(JsonSerializer.Serialize(new ResearchShadowCampaignReport
        {
            ReportHash = new string('a', 64)
        }))!.AsObject();
        shadowJson.Remove(nameof(ResearchShadowCampaignReport.ValidationPolicyVersion));
        var restoredShadow = shadowJson.Deserialize<ResearchShadowCampaignReport>();
        Assert.Equal("legacy-unversioned", restoredShadow!.ValidationPolicyVersion);
    }
}
