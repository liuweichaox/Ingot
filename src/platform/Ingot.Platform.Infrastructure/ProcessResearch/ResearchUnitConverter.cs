namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
/// Small, deterministic conversion boundary for common industrial units. Unknown
/// or domain-specific units intentionally remain strict strings instead of being
/// guessed by the platform.
/// </summary>
public static class ResearchUnitConverter
{
    public static bool TryConvert(double value, string? sourceUnit, string? targetUnit, out double converted)
    {
        converted = default;
        var source = Normalize(sourceUnit);
        var target = Normalize(targetUnit);
        if (source.Length == 0 || target.Length == 0) return false;
        if (source == target)
        {
            converted = value;
            return true;
        }
        if (TryPressure(source, out var sourceMpa) && TryPressure(target, out var targetMpa))
        {
            converted = value * sourceMpa / targetMpa;
            return true;
        }
        if (TryLength(source, out var sourceMillimeters) && TryLength(target, out var targetMillimeters))
        {
            converted = value * sourceMillimeters / targetMillimeters;
            return true;
        }
        if (IsCelsius(source) && IsKelvin(target))
        {
            converted = value + 273.15;
            return true;
        }
        if (IsKelvin(source) && IsCelsius(target))
        {
            converted = value - 273.15;
            return true;
        }
        return false;
    }

    private static string Normalize(string? value)
        => (value ?? "").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal)
            .Replace("μ", "u", StringComparison.Ordinal).Replace("µ", "u", StringComparison.Ordinal);

    private static bool TryPressure(string unit, out double mpa)
    {
        mpa = unit switch
        {
            "mpa" => 1d,
            "kpa" => 0.001d,
            "pa" => 0.000001d,
            "bar" => 0.1d,
            "kgf/cm2" or "kgf/cm²" => 0.0980665d,
            _ => 0d
        };
        return mpa > 0;
    }

    private static bool TryLength(string unit, out double millimeters)
    {
        millimeters = unit switch
        {
            "m" => 1000d,
            "cm" => 10d,
            "mm" => 1d,
            "um" => 0.001d,
            "nm" => 0.000001d,
            _ => 0d
        };
        return millimeters > 0;
    }

    private static bool IsCelsius(string unit) => unit is "c" or "°c" or "℃";
    private static bool IsKelvin(string unit) => unit is "k" or "kelvin";
}
