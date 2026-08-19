namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
/// Compatibility facade for callers that have not yet moved to the Platform Application assembly.
/// </summary>
public static class ResearchUnitConverter
{
    public static bool TryConvert(double value, string? sourceUnit, string? targetUnit, out double converted)
        => Application.ProcessResearch.ResearchUnitConverter.TryConvert(
            value, sourceUnit, targetUnit, out converted);
}
