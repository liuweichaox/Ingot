using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
///     Pre-registered calibration gates used independently by historical replay, shadow validation,
///     and online admission. Values preserve existing behavior and await production validation.
/// </summary>
public static class ValidationThresholds
{
    /// <summary>Minimum calibration observations before coverage can trigger a decision.</summary>
    public const int MinimumCalibrationCheckCount = 5;

    /// <summary>Minimum acceptable empirical coverage for the declared prediction interval.</summary>
    public const double MinimumCalibrationCoverage = 0.8;

    public static readonly string PolicyVersion =
        $"research-validation-v1+{ComputeFingerprint()}";

    public static string ComputeFingerprint()
    {
        var canonical = string.Join('|',
            MinimumCalibrationCheckCount.ToString(CultureInfo.InvariantCulture),
            MinimumCalibrationCoverage.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..8];
    }
}
