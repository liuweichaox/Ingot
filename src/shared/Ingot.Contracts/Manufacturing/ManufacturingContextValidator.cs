using System.Text.RegularExpressions;

namespace Ingot.Contracts.Manufacturing;

public static partial class ManufacturingContextValidator
{
    private static readonly string[] Sources = ["manual", "mes", "device", "import"];

    public static bool TryValidate(
        ToolingComponentTypeDefinition? value,
        out ToolingComponentTypeDefinition? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("组件类型不能为空。", out error);
        if (!TryCode(value.ComponentTypeCode, "ComponentTypeCode", out var code, out error))
            return false;
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        var status = Normalize(value.Status)?.ToLowerInvariant() ?? "active";
        if (status is not ("active" or "inactive"))
            return Fail("Status 只能是 active 或 inactive。", out error);
        normalized = value with
        {
            ComponentTypeCode = code!,
            Name = name,
            Status = status,
            Attributes = NormalizeAttributes(value.Attributes),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ToolingTypeDefinition? value,
        out ToolingTypeDefinition? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("工装类型不能为空。", out error);
        if (!TryCode(value.ToolingTypeCode, "ToolingTypeCode", out var typeCode, out error))
            return false;
        if (value.Version <= 0)
            return Fail("Version 必须大于 0。", out error);
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        if (value.Roles is null || value.Roles.Count is 0 or > 100)
            return Fail("Roles 必须包含 1 到 100 项。", out error);
        var status = Normalize(value.Status)?.ToLowerInvariant() ?? "active";
        if (status is not ("active" or "inactive"))
            return Fail("Status 只能是 active 或 inactive。", out error);

        var roles = new List<ToolingRoleDefinition>();
        foreach (var role in value.Roles)
        {
            if (role is null || !TryCode(role.Code, "Role.Code", out var code, out error))
                return false;
            var roleName = Normalize(role.Name);
            if (roleName is null || roleName.Length > 200)
                return Fail("Role.Name 不能为空且最长 200 个字符。", out error);
            if (role.MaxCount is < 1 or > 100)
                return Fail("Role.MaxCount 必须在 1 到 100 之间。", out error);
            var acceptedComponentTypes = new List<string>();
            foreach (var componentTypeCode in role.AcceptedComponentTypeCodes ?? [])
            {
                if (!TryCode(componentTypeCode, "Role.AcceptedComponentTypeCodes", out var acceptedCode, out error))
                    return false;
                acceptedComponentTypes.Add(acceptedCode!);
            }
            if (acceptedComponentTypes.Distinct(StringComparer.Ordinal).Count() != acceptedComponentTypes.Count)
                return Fail("Role.AcceptedComponentTypeCodes 不能包含重复代码。", out error);
            roles.Add(role with
            {
                Code = code!,
                Name = roleName,
                AcceptedComponentTypeCodes = acceptedComponentTypes.Order(StringComparer.Ordinal).ToArray()
            });
        }
        if (roles.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() != roles.Count)
            return Fail("Roles 不能包含重复 Code。", out error);

        normalized = value with
        {
            ToolingTypeCode = typeCode!,
            Name = name,
            Status = status,
            Roles = roles.OrderBy(static item => item.SortOrder).ThenBy(static item => item.Code).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ToolingComponent? value,
        out ToolingComponent? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("工装组件不能为空。", out error);
        if (!TryId(value.ComponentId, "ComponentId", out var componentId, out error) ||
            !TryCode(value.ComponentTypeCode, "ComponentTypeCode", out var componentTypeCode, out error) ||
            !TryId(value.SerialNo, "SerialNo", out var serialNo, out error))
        {
            return false;
        }
        var status = Normalize(value.Status)?.ToLowerInvariant() ?? "available";
        if (status is not ("available" or "maintenance" or "retired"))
            return Fail("Status 只能是 available、maintenance 或 retired。", out error);
        normalized = value with
        {
            ComponentId = componentId!,
            ComponentTypeCode = componentTypeCode!,
            SerialNo = serialNo!,
            Name = Normalize(value.Name),
            Status = status,
            Attributes = NormalizeAttributes(value.Attributes),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ToolingAssembly? value,
        out ToolingAssembly? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("模具不能为空。", out error);
        if (!TryId(value.MoldId, "MoldId", out var moldId, out error) ||
            !TryCode(value.ToolingTypeCode, "ToolingTypeCode", out var typeCode, out error))
        {
            return false;
        }
        var name = Normalize(value.Name);
        if (name is null || name.Length > 200)
            return Fail("Name 不能为空且最长 200 个字符。", out error);
        var status = Normalize(value.Status)?.ToLowerInvariant() ?? "active";
        if (status is not ("active" or "inactive"))
            return Fail("Status 只能是 active 或 inactive。", out error);
        normalized = value with
        {
            MoldId = moldId!,
            ToolingTypeCode = typeCode!,
            Name = name,
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ToolingAssemblyRevision? value,
        out ToolingAssemblyRevision? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("模具组合版本不能为空。", out error);
        if (!TryId(value.MoldId, "MoldId", out var moldId, out error))
            return false;
        if (value.Revision <= 0)
            return Fail("Revision 必须大于 0。", out error);
        if (value.Members is null || value.Members.Count is 0 or > 100)
            return Fail("Members 必须包含 1 到 100 项。", out error);
        var members = new List<ToolingAssemblyMember>();
        foreach (var member in value.Members)
        {
            if (member is null ||
                !TryCode(member.RoleCode, "Member.RoleCode", out var roleCode, out error) ||
                !TryId(member.ComponentId, "Member.ComponentId", out var componentId, out error))
            {
                return false;
            }
            members.Add(member with { RoleCode = roleCode!, ComponentId = componentId! });
        }
        if (members.Select(static item => item.RoleCode).Distinct(StringComparer.Ordinal).Count() != members.Count)
            return Fail("同一个组合版本中每个角色只能出现一次。", out error);
        normalized = value with
        {
            AssemblyRevisionId = value.AssemblyRevisionId == Guid.Empty ? Guid.NewGuid() : value.AssemblyRevisionId,
            MoldId = moldId!,
            Members = members.OrderBy(static item => item.RoleCode).ToArray(),
            CreatedBy = Normalize(value.CreatedBy),
            CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt.ToUniversalTime()
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ToolingInstallation? value,
        out ToolingInstallation? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("装模记录不能为空。", out error);
        if (!TryId(value.MachineId, "MachineId", out var machineId, out error))
            return false;
        if (value.AssemblyRevisionId == Guid.Empty)
            return Fail("AssemblyRevisionId 不能为空。", out error);
        if (value.RemovedAt.HasValue && value.RemovedAt <= value.InstalledAt)
            return Fail("RemovedAt 必须晚于 InstalledAt。", out error);
        if (!TrySource(value.Source, out var source, out error))
            return false;
        normalized = value with
        {
            InstallationId = value.InstallationId == Guid.Empty ? Guid.NewGuid() : value.InstallationId,
            MachineId = machineId!,
            InstalledAt = value.InstalledAt == default ? DateTimeOffset.UtcNow : value.InstalledAt.ToUniversalTime(),
            RemovedAt = value.RemovedAt?.ToUniversalTime(),
            Source = source!,
            CommandId = Normalize(value.CommandId),
            Actor = Normalize(value.Actor),
            CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt.ToUniversalTime()
        };
        return Succeed(out error);
    }

    public static bool TryValidate(
        ProductionContext? value,
        out ProductionContext? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("生产上下文不能为空。", out error);
        if (!TryId(value.MachineId, "MachineId", out var machineId, out error) ||
            !TryId(value.ProductSeries, "ProductSeries", out var productSeries, out error) ||
            !TryId(value.ProductCode, "ProductCode", out var productCode, out error) ||
            !TryId(value.RecipeId, "RecipeId", out var recipeId, out error) ||
            !TryId(value.RecipeVersion, "RecipeVersion", out var recipeVersion, out error))
        {
            return false;
        }
        if (value.ToolingInstallationId == Guid.Empty)
            return Fail("ToolingInstallationId 不能为空。", out error);
        if (value.ValidTo.HasValue && value.ValidTo <= value.ValidFrom)
            return Fail("ValidTo 必须晚于 ValidFrom。", out error);
        if (!TrySource(value.Source, out var source, out error))
            return false;
        if (source == "mes" && string.IsNullOrWhiteSpace(value.CommandId))
            return Fail("MES 写入生产上下文时必须提供 CommandId，以保证重复同步不会重复建档。", out error);
        normalized = value with
        {
            ContextId = value.ContextId == Guid.Empty ? Guid.NewGuid() : value.ContextId,
            MachineId = machineId!,
            ProductSeries = productSeries!,
            ProductCode = productCode!,
            RecipeId = recipeId!,
            RecipeVersion = recipeVersion!,
            ValidFrom = value.ValidFrom == default ? DateTimeOffset.UtcNow : value.ValidFrom.ToUniversalTime(),
            ValidTo = value.ValidTo?.ToUniversalTime(),
            Source = source!,
            CommandId = Normalize(value.CommandId),
            ExternalOrderRef = Normalize(value.ExternalOrderRef),
            ExternalBatchRef = Normalize(value.ExternalBatchRef),
            MaterialLotRef = Normalize(value.MaterialLotRef),
            Actor = Normalize(value.Actor),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Succeed(out error);
    }

    private static IReadOnlyDictionary<string, string> NormalizeAttributes(
        IReadOnlyDictionary<string, string>? attributes)
        => (attributes ?? new Dictionary<string, string>())
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value.Trim(), StringComparer.Ordinal);

    private static bool TrySource(string? value, out string? source, out string error)
    {
        source = Normalize(value)?.ToLowerInvariant() ?? "manual";
        return Sources.Contains(source, StringComparer.Ordinal)
            ? Succeed(out error)
            : Fail("Source 只能是 manual、mes、device 或 import。", out error);
    }

    private static bool TryCode(string? value, string name, out string? normalized, out string error)
    {
        normalized = Normalize(value)?.ToLowerInvariant();
        return normalized is not null && CodePattern().IsMatch(normalized)
            ? Succeed(out error)
            : Fail($"{name} 必须是小写点分标识，长度 1 到 128。", out error);
    }

    private static bool TryId(string? value, string name, out string? normalized, out string error)
    {
        normalized = Normalize(value);
        return normalized is not null && IdPattern().IsMatch(normalized)
            ? Succeed(out error)
            : Fail($"{name} 只能包含字母、数字、点、下划线、斜杠和连字符，长度 1 到 128。", out error);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
