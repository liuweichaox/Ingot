using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>
///     Versioned policy inputs for execution comparison and evidence grading. These values preserve
///     the existing behavior; they have not yet been calibrated with the stage-1 production dataset.
/// </summary>
public static class ProcessExecutionAnalysisThresholds
{
    /// <summary>Observational candidate score weight. Pending stage-1 production calibration.</summary>
    public const double CandidateScoreWeight = 0.4;

    /// <summary>Adjusted model rank score weight. Pending stage-1 production calibration.</summary>
    public const double ModelRankScoreWeight = 0.6;

    /// <summary>Selection rate classified as stable evidence. Pending stage-1 production calibration.</summary>
    public const double HighStabilitySelectionRate = 0.6;

    /// <summary>Selection rate classified as exploratory evidence. Pending stage-1 production calibration.</summary>
    public const double ModerateStabilitySelectionRate = 0.25;

    /// <summary>Signal coverage below this value degrades data quality. Pending stage-1 production calibration.</summary>
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
