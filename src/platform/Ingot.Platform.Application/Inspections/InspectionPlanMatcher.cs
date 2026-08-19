using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

public static class InspectionPlanMatcher
{
    public static InspectionPlan? Resolve(
        IEnumerable<InspectionPlan> plans,
        IReadOnlyDictionary<string, string> context,
        string equipmentId,
        DateTimeOffset occurredAt)
        => plans
            .Where(static plan => plan.Status is InspectionPlanStatuses.Published or InspectionPlanStatuses.Retired)
            .Where(plan => (!plan.EffectiveFrom.HasValue || plan.EffectiveFrom <= occurredAt) &&
                           (!plan.EffectiveTo.HasValue || plan.EffectiveTo > occurredAt))
            .Where(plan => Matches(plan.Scope, context, equipmentId))
            .OrderByDescending(static plan => plan.Priority)
            .ThenByDescending(static plan => Specificity(plan.Scope))
            .ThenByDescending(static plan => plan.Version)
            .ThenByDescending(static plan => plan.UpdatedAt)
            .FirstOrDefault();

    private static bool Matches(
        InspectionPlanScope scope,
        IReadOnlyDictionary<string, string> context,
        string equipmentId)
        => Matches(scope.ProductFamilyCode, context.GetValueOrDefault("product_family_code")) &&
           Matches(scope.ProductCode, context.GetValueOrDefault("product_code")) &&
           Matches(scope.ProcessSpecificationId, context.GetValueOrDefault("process_specification_id")) &&
           Matches(scope.EquipmentId, equipmentId) &&
           scope.ContextSelector.All(pair => Matches(
               pair.Value,
               pair.Key == "equipment_id" ? equipmentId : ContextValue(context, pair.Key)));

    private static bool Matches(string? selector, string? value)
        => string.IsNullOrWhiteSpace(selector) ||
           string.Equals(selector, value, StringComparison.OrdinalIgnoreCase);

    private static int Specificity(InspectionPlanScope scope)
        => new[] { scope.ProductFamilyCode, scope.ProductCode, scope.ProcessSpecificationId, scope.EquipmentId }
            .Count(static value => !string.IsNullOrWhiteSpace(value)) + scope.ContextSelector.Count;

    private static string? ContextValue(IReadOnlyDictionary<string, string> context, string key)
        => context.GetValueOrDefault(key) ?? context.GetValueOrDefault(key.Replace('.', '_'));
}
