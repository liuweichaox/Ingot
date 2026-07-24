using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed record ProcessFeatureDefinition
{
    public required string Code { get; init; }
    public int Version { get; init; } = 1;
    public required string Operator { get; init; }
    public required string Weighting { get; init; }
    public required string OutputUnitRule { get; init; }
    public int MinimumPointCount { get; init; }
    public bool SupportsPhase { get; init; } = true;
    public required string DefinitionHash { get; init; }
}

public interface IFeatureDefinitionRegistry
{
    ProcessFeatureDefinition GetRequired(string code);
    IReadOnlyList<ProcessFeatureDefinition> List();
}

/// <summary>
/// Immutable reference definitions for the deterministic feature engine. A definition
/// version or semantic field change produces a different hash and therefore a new
/// reproducibility identity, even when the display code remains unchanged.
/// </summary>
public sealed class BuiltInFeatureDefinitionRegistry : IFeatureDefinitionRegistry
{
    private readonly IReadOnlyDictionary<string, ProcessFeatureDefinition> _definitions;

    public BuiltInFeatureDefinitionRegistry()
    {
        _definitions = new[]
            {
                Create("mean", "time_weighted_mean", "duration", "same-as-input", 2),
                Create("average", "time_weighted_mean", "duration", "same-as-input", 2),
                Create("min", "minimum", "sample", "same-as-input", 1),
                Create("minimum", "minimum", "sample", "same-as-input", 1),
                Create("max", "maximum", "sample", "same-as-input", 1),
                Create("maximum", "maximum", "sample", "same-as-input", 1),
                Create("range", "range", "sample", "same-as-input", 1),
                Create("std", "time_weighted_standard_deviation", "duration", "same-as-input", 2),
                Create("stddev", "time_weighted_standard_deviation", "duration", "same-as-input", 2),
                Create("median", "weighted_percentile_50", "duration", "same-as-input", 1),
                Create("p05", "weighted_percentile_05", "duration", "same-as-input", 1),
                Create("p95", "weighted_percentile_95", "duration", "same-as-input", 1),
                Create("integral", "trapezoid_integral", "duration", "input-times-second", 2),
                Create("slope", "weighted_linear_slope", "duration", "input-per-second", 2)
            }
            .ToDictionary(static definition => definition.Code, StringComparer.Ordinal);
    }

    public ProcessFeatureDefinition GetRequired(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return _definitions.TryGetValue(normalized, out var definition)
            ? definition
            : throw new InvalidOperationException($"未注册的科研特征定义：{code}。");
    }

    public IReadOnlyList<ProcessFeatureDefinition> List()
        => _definitions.Values.OrderBy(static definition => definition.Code, StringComparer.Ordinal).ToArray();

    private static ProcessFeatureDefinition Create(
        string code,
        string @operator,
        string weighting,
        string outputUnitRule,
        int minimumPointCount)
    {
        const int version = 1;
        var canonical = string.Join(
            "|",
            code,
            version,
            @operator,
            weighting,
            outputUnitRule,
            minimumPointCount,
            "phase=true");
        return new ProcessFeatureDefinition
        {
            Code = code,
            Version = version,
            Operator = @operator,
            Weighting = weighting,
            OutputUnitRule = outputUnitRule,
            MinimumPointCount = minimumPointCount,
            DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant()
        };
    }
}
