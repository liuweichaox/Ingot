using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Application.ProcessExecutions;

public static class ProcessExecutionAnalysisThresholds
{

    public const double CandidateScoreWeight = 0.4;

    public const double ModelRankScoreWeight = 0.6;

    public const double HighStabilitySelectionRate = 0.6;

    public const double ModerateStabilitySelectionRate = 0.25;

    public const double MinimumSignalCoverage = 0.95;

    public static string ComputeFingerprint()
    {
        var canonical = string.Join('|',
            CandidateScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            ModelRankScoreWeight.ToString("R", CultureInfo.InvariantCulture),
            HighStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            ModerateStabilitySelectionRate.ToString("R", CultureInfo.InvariantCulture),
            MinimumSignalCoverage.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];
    }
}
