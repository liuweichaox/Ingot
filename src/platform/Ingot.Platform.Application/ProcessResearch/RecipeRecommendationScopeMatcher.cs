namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>Matches a run against the selected recommendation scope, including explicitly missing values.</summary>
public static class RecipeRecommendationScopeMatcher
{
    /// <summary>Returns whether the actual value satisfies the specified scope field.</summary>
    public static bool Matches(IReadOnlyDictionary<string, string> context, string key, string? actual)
    {
        if (context.TryGetValue("missing_scope_fields", out var missingFields) &&
            missingFields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(key, StringComparer.Ordinal))
            return string.IsNullOrWhiteSpace(actual);

        return !context.TryGetValue(key, out var expected) || string.IsNullOrWhiteSpace(expected) ||
            string.Equals(expected.Trim(), actual?.Trim(), StringComparison.Ordinal);
    }
}
