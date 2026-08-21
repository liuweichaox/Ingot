
namespace Ingot.Contracts.Inspections;

public static class InspectionCharacteristicOutcomeEvaluator
{
    public static string Evaluate(InspectionCharacteristicDefinition definition, string value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        value = value.Trim();
        if (definition.PassingValues.Count == 0)
            return "INCONCLUSIVE";
        return definition.PassingValues.Contains(value, StringComparer.Ordinal) ? "PASS" : "FAIL";
    }
}
