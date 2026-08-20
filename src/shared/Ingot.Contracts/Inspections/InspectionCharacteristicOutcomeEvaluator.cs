// 说明 InspectionCharacteristicOutcomeEvaluator 在所属模块中的职责、输入边界和失败语义。

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
