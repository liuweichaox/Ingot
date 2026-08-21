
using System.Text.RegularExpressions;

namespace Ingot.Contracts.Inspections;

public static partial class InspectionMasterDataValidator
{
    public static bool TryValidate(InspectionDefinition? value, out InspectionDefinition? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("检测定义不能为空。", out error);
        if (!TryCode(value.Code, "Code", out var code, out error))
            return false;
        if (value.Version <= 0)
            return Fail("Version 必须大于 0。", out error);
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        if (value.Characteristics is null || value.Characteristics.Count is 0 or > 200)
            return Fail("Characteristics 必须包含 1 到 200 项。", out error);

        var characteristics = new List<InspectionCharacteristicDefinition>();
        foreach (var characteristic in value.Characteristics)
        {
            if (characteristic is null ||
                !TryCode(characteristic.Code, "Characteristic.Code", out var characteristicCode, out error))
            {
                return false;
            }

            var characteristicName = Normalize(characteristic.Name);
            if (characteristicName is null || characteristicName.Length > 200)
                return Fail("Characteristic.Name 不能为空且最长 200 个字符。", out error);
            var inputType = Normalize(characteristic.InputType)?.ToLowerInvariant();
            if (inputType is not ("numeric" or "text" or "select" or "boolean"))
                return Fail("Characteristic.InputType 只能是 numeric、text、select 或 boolean。", out error);
            if (characteristic.LowerLimit.HasValue && characteristic.UpperLimit.HasValue &&
                characteristic.LowerLimit > characteristic.UpperLimit)
            {
                return Fail("Characteristic.LowerLimit 不能大于 UpperLimit。", out error);
            }

            var allowedValues = (characteristic.AllowedValues ?? [])
                .Select(Normalize)
                .Where(static item => item is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (inputType == "select" && allowedValues.Length == 0)
                return Fail("选择型检测特性必须配置至少一个可选值。", out error);
            if (allowedValues.Length > 100 || allowedValues.Any(static item => item.Length > 200))
                return Fail("Characteristic.AllowedValues 最多 100 项，每项最长 200 个字符。", out error);

            var passingValues = (characteristic.PassingValues ?? [])
                .Select(Normalize)
                .Where(static item => item is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (inputType is "select" or "boolean" && passingValues.Length == 0)
                return Fail("选择型和是/否型检测特性必须明确配置合格值。", out error);
            if (passingValues.Length > 100 || passingValues.Any(static item => item.Length > 200))
                return Fail("Characteristic.PassingValues 最多 100 项，每项最长 200 个字符。", out error);
            if (inputType == "select" && passingValues.Any(item => !allowedValues.Contains(item, StringComparer.Ordinal)))
                return Fail("Characteristic.PassingValues 必须属于 AllowedValues。", out error);
            if (inputType == "boolean" && passingValues.Any(static item => item is not ("true" or "false")))
                return Fail("是/否型检测特性的合格值只能是 true 或 false。", out error);

            characteristics.Add(characteristic with
            {
                Code = characteristicCode!,
                Name = characteristicName,
                InputType = inputType,
                Unit = inputType == "numeric" ? Normalize(characteristic.Unit) : null,
                LowerLimit = inputType == "numeric" ? characteristic.LowerLimit : null,
                UpperLimit = inputType == "numeric" ? characteristic.UpperLimit : null,
                AllowedValues = inputType == "select" ? allowedValues : [],
                PassingValues = inputType == "numeric" ? [] : passingValues
            });
        }

        if (characteristics.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() !=
            characteristics.Count)
        {
            return Fail("Characteristics 不能包含重复 Code。", out error);
        }

        normalized = value with
        {
            Code = code!,
            Name = name,
            Description = Normalize(value.Description),
            Characteristics = characteristics.OrderBy(static item => item.Code, StringComparer.Ordinal).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(PhaseDefinition? value, out PhaseDefinition? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("阶段定义不能为空。", out error);
        if (!TryCode(value.Code, "Code", out var code, out error))
            return false;
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        normalized = value with { Code = code!, Name = name, UpdatedAt = DateTimeOffset.UtcNow };
        return Succeed(out error);
    }

    public static bool TryValidate(InspectionPlan? value, out InspectionPlan? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("质量方案不能为空。", out error);
        if (!TryId(value.PlanId, "PlanId", out var planId, out error))
            return false;
        if (value.Version <= 0)
            return Fail("Version 必须大于 0。", out error);
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        var status = Normalize(value.Status)?.ToLowerInvariant() ?? InspectionPlanStatuses.Draft;
        if (!InspectionPlanStatuses.IsValid(status))
            return Fail("Status 只能是 draft、published 或 retired。", out error);
        if (value.Items is null || value.Items.Count is 0 or > 100)
            return Fail("Items 必须包含 1 到 100 项。", out error);
        if (value.EffectiveFrom.HasValue && value.EffectiveTo.HasValue && value.EffectiveFrom >= value.EffectiveTo)
            return Fail("EffectiveFrom 必须早于 EffectiveTo。", out error);
        if (status == InspectionPlanStatuses.Retired && !value.EffectiveTo.HasValue)
            return Fail("停用方案必须设置 EffectiveTo。", out error);

        var items = new List<InspectionPlanItem>();
        foreach (var item in value.Items)
        {
            if (item is null || !TryCode(item.DefinitionCode, "Item.DefinitionCode", out var definitionCode, out error))
                return false;
            if (item.DefinitionVersion <= 0)
                return Fail("Item.DefinitionVersion 必须大于 0。", out error);
            items.Add(item with
            {
                DefinitionCode = definitionCode!,
                RequiresAttachment = item.RequiresAttachment || item.RequiresReview
            });
        }

        if (items.Select(static item => (item.DefinitionCode, item.DefinitionVersion))
            .Distinct().Count() != items.Count)
        {
            return Fail("Items 不能包含重复的检测定义版本。", out error);
        }

        var scope = value.Scope ?? new InspectionPlanScope();
        var contextSelector = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in scope.ContextSelector ?? new Dictionary<string, string>())
        {
            if (!TryCode(pair.Key, "Scope.ContextSelector.Key", out var key, out error) ||
                string.IsNullOrWhiteSpace(pair.Value))
                return Fail("质量方案的自定义适用条件必须包含有效的键和值。", out error);
            contextSelector[key!] = pair.Value.Trim();
        }
        normalized = value with
        {
            PlanId = planId!.ToLowerInvariant(),
            Name = name,
            Description = Normalize(value.Description),
            Status = status,
            Scope = scope with
            {
                ProductFamilyCode = NormalizeSelector(scope.ProductFamilyCode),
                ProductCode = NormalizeSelector(scope.ProductCode),
                ProcessSpecificationId = NormalizeSelector(scope.ProcessSpecificationId),
                EquipmentId = NormalizeSelector(scope.EquipmentId),
                ContextSelector = contextSelector
            },
            Items = items.OrderBy(static item => item.Sequence)
                .ThenBy(static item => item.DefinitionCode, StringComparer.Ordinal)
                .ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(PhaseMapping? value, out PhaseMapping? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("阶段映射不能为空。", out error);
        if (!TryId(value.ProcessSpecificationId, "ProcessSpecificationId", out var processSpecificationId, out error) ||
            !TryId(value.ProcessStep, "ProcessStep", out var processStep, out error) ||
            !TryCode(value.PhaseCode, "PhaseCode", out var phaseCode, out error))
        {
            return false;
        }

        var phaseSource = Normalize(value.PhaseSource)?.ToLowerInvariant() ?? "process-specification";
        if (phaseSource is not ("process-specification" or "equipment" or "estimated"))
            return Fail("阶段来源只能是 process-specification、equipment 或 estimated。", out error);
        var processSpecificationVersion = Normalize(value.ProcessSpecification);
        var processTemplate = Normalize(value.ProcessTemplate);
        var mappingId = string.Join(
            ":",
            processSpecificationId,
            processSpecificationVersion ?? "*",
            processTemplate ?? "*",
            processStep).ToLowerInvariant();

        normalized = value with
        {
            MappingId = string.IsNullOrWhiteSpace(value.MappingId) ? mappingId : value.MappingId.Trim().ToLowerInvariant(),
            ProcessSpecificationId = processSpecificationId!,
            ProcessSpecification = processSpecificationVersion,
            ProcessTemplate = processTemplate,
            ProcessStep = processStep!,
            ProcessStepName = Normalize(value.ProcessStepName),
            PhaseCode = phaseCode!,
            PhaseSource = phaseSource,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(FeatureDefinition? value, out FeatureDefinition? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("特征定义不能为空。", out error);
        if (!TryCode(value.Code, "Code", out var code, out error) ||
            !TryCode(value.PhaseCode, "PhaseCode", out var phaseCode, out error))
        {
            return false;
        }

        var name = Normalize(value.Name);
        var signal = Normalize(value.Signal)?.ToLowerInvariant();
        var aggregation = Normalize(value.Aggregation)?.ToLowerInvariant();
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        if (signal is null || signal.Length > 200)
            return Fail("Signal 不能为空且最长 200 个字符。", out error);
        if (aggregation is not ("mean" or "min" or "max" or "slope" or "slope_deviation" or "integral" or "dwell" or "range_across"))
            return Fail("Aggregation 不在支持范围内。", out error);
        var boundaryMode = Normalize(value.BoundaryMode)?.ToLowerInvariant() ??
                           (aggregation is "slope" or "slope_deviation" or "integral" ? "include_leading" : "strict");
        if (boundaryMode is not ("strict" or "include_leading"))
            return Fail("BoundaryMode 只能是 strict 或 include_leading。", out error);

        normalized = value with
        {
            Code = code!,
            Name = name,
            PhaseCode = phaseCode!,
            Signal = signal,
            Aggregation = aggregation,
            BoundaryMode = boundaryMode,
            Unit = Normalize(value.Unit),
            ProductFamilyCode = NormalizeSelector(value.ProductFamilyCode),
            ProductCode = NormalizeSelector(value.ProductCode),
            ProcessSpecificationId = NormalizeSelector(value.ProcessSpecificationId),
            EquipmentId = NormalizeSelector(value.EquipmentId),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    private static bool TryCode(string? value, string name, out string? normalized, out string error)
    {
        normalized = Normalize(value)?.ToLowerInvariant();
        if (normalized is null || !CodePattern().IsMatch(normalized))
            return Fail($"{name} 必须是小写点分标识，长度 1 到 128。", out error);
        return Succeed(out error);
    }

    private static bool TryId(string? value, string name, out string? normalized, out string error)
    {
        normalized = Normalize(value);
        if (normalized is null || !IdPattern().IsMatch(normalized))
            return Fail($"{name} 只能包含字母、数字、点、下划线、斜杠和连字符，长度 1 到 128。", out error);
        return Succeed(out error);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSelector(string? value)
        => Normalize(value)?.ToLowerInvariant();

    private static bool Succeed(out string error)
    {
        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]*(?:\\.[a-z0-9][a-z0-9_-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_./-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
