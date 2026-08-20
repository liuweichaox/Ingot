// 验证平台组件 AnalysisThresholdVersion 的成功、拒绝和安全边界。

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AnalysisThresholdVersionTests
{
    [Fact]
    public void ProcessExecutionAlgorithmVersion_ContainsFingerprintOfEveryDecisionThreshold()
    {
        var canonical = string.Join('|',
            ProcessExecutionAnalysisThresholds.CandidateScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            ProcessExecutionAnalysisThresholds.ModelRankScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            ProcessExecutionAnalysisThresholds.HighStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            ProcessExecutionAnalysisThresholds.ModerateStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            ProcessExecutionAnalysisThresholds.MinimumSignalCoverage.ToString("R", CultureInfo.InvariantCulture));
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];

        Assert.Equal(expected, ProcessExecutionAnalysisThresholds.ComputeFingerprint());
        Assert.Equal($"stage-relative-v6+{expected}", ProcessExecutionAnalysisEngine.AlgorithmVersion);
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

}
