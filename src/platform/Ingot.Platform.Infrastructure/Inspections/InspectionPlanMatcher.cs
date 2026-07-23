using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public static class InspectionPlanMatcher
{
    public static InspectionPlan? Resolve(
        IEnumerable<InspectionPlan> plans,
        IReadOnlyDictionary<string, string> context,
        string machineId,
        DateTimeOffset occurredAt)
        => plans
            .Where(static plan => plan.Status is InspectionPlanStatuses.Published or InspectionPlanStatuses.Retired)
            .Where(plan => (!plan.EffectiveFrom.HasValue || plan.EffectiveFrom <= occurredAt) &&
                           (!plan.EffectiveTo.HasValue || plan.EffectiveTo > occurredAt))
            .Where(plan => Matches(plan.Scope, context, machineId))
            .OrderByDescending(static plan => plan.Priority)
            .ThenByDescending(static plan => Specificity(plan.Scope))
            .ThenByDescending(static plan => plan.Version)
            .ThenByDescending(static plan => plan.UpdatedAt)
            .FirstOrDefault();

    private static bool Matches(
        InspectionPlanScope scope,
        IReadOnlyDictionary<string, string> context,
        string machineId)
        => Matches(scope.ProductSeries, context.GetValueOrDefault("product_series")) &&
           Matches(scope.ProductCode, context.GetValueOrDefault("product_code")) &&
           Matches(scope.RecipeId, context.GetValueOrDefault("recipe_id")) &&
           Matches(scope.MachineId, machineId) &&
           scope.ContextSelector.All(pair => Matches(
               pair.Value,
               pair.Key == "machine_id" ? machineId : ContextValue(context, pair.Key)));

    private static bool Matches(string? selector, string? value)
        => string.IsNullOrWhiteSpace(selector) ||
           string.Equals(selector, value, StringComparison.OrdinalIgnoreCase);

    private static int Specificity(InspectionPlanScope scope)
        => new[] { scope.ProductSeries, scope.ProductCode, scope.RecipeId, scope.MachineId }
            .Count(static value => !string.IsNullOrWhiteSpace(value)) + scope.ContextSelector.Count;

    private static string? ContextValue(IReadOnlyDictionary<string, string> context, string key)
        => context.GetValueOrDefault(key) ?? context.GetValueOrDefault(key.Replace('.', '_'));
}
