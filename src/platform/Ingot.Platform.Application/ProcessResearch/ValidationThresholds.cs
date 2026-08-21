using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Application.ProcessResearch;

public static class ValidationThresholds
{

    public const int MinimumCalibrationCheckCount = 5;

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
