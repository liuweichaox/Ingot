using Ingot.Contracts.Manufacturing;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class ManufacturingContextValidatorTests
{
    [Fact]
    public void ComponentType_NormalizesConfigurableClassification()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ToolingComponentTypeDefinition
            {
                ComponentTypeCode = "MOLD_CORE",
                Name = "模芯"
            },
            out ToolingComponentTypeDefinition? normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("mold_core", normalized!.ComponentTypeCode);
        Assert.Equal("模芯", normalized.Name);
    }

    [Fact]
    public void ToolingType_NormalizesRolesWithoutKnowingIndustrySemantics()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ToolingTypeDefinition
            {
                ToolingTypeCode = "OPTICAL-GLASS-MOLD",
                Name = "光学模具",
                Roles =
                [
                    new ToolingRoleDefinition
                    {
                        Code = "UPPER_CORE",
                        Name = "上模芯",
                        SortOrder = 1,
                        AcceptedComponentTypeCodes = ["MOLD_CORE"]
                    },
                    new ToolingRoleDefinition { Code = "LOWER_CORE", Name = "下模芯", SortOrder = 2 }
                ]
            },
            out ToolingTypeDefinition? normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("optical-glass-mold", normalized!.ToolingTypeCode);
        Assert.Equal("upper_core", normalized.Roles[0].Code);
        Assert.Equal("mold_core", normalized.Roles[0].AcceptedComponentTypeCodes[0]);
    }

    [Fact]
    public void ToolingComponent_IsClassifiedWithoutBeingBoundToAnAssemblyRole()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ToolingComponent
            {
                ComponentId = "PART-01",
                ComponentTypeCode = "MOLD_CORE",
                SerialNo = "SN-001",
                Name = "可复用模芯"
            },
            out ToolingComponent? normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("mold_core", normalized!.ComponentTypeCode);
    }

    [Fact]
    public void ToolingComponent_RejectsUnknownLifecycleStatus()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ToolingComponent
            {
                ComponentId = "PART-01",
                ComponentTypeCode = "MOLD_CORE",
                SerialNo = "SN-001",
                Status = "deleted"
            },
            out ToolingComponent? _,
            out var error);

        Assert.False(ok);
        Assert.Contains("available", error);
    }

    [Fact]
    public void AssemblyRevision_RejectsDuplicateRoles()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ToolingAssemblyRevision
            {
                MoldId = "MOLD-01",
                Members =
                [
                    new ToolingAssemblyMember { RoleCode = "upper_core", ComponentId = "UP-01" },
                    new ToolingAssemblyMember { RoleCode = "upper_core", ComponentId = "UP-02" }
                ]
            },
            out ToolingAssemblyRevision? _,
            out var error);

        Assert.False(ok);
        Assert.Contains("每个角色只能出现一次", error);
    }

    [Fact]
    public void ProductionContext_GeneratesIdentityAndKeepsExternalGroupingOptional()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ProductionContext
            {
                MachineId = "PRESS-01",
                ProductSeries = "LENS-A",
                ProductCode = "LENS-A-01",
                RecipeId = "RCP-A",
                RecipeVersion = "7",
                ToolingInstallationId = Guid.NewGuid(),
                Source = "MES",
                CommandId = "MES-CMD-001"
            },
            out ProductionContext? normalized,
            out var error);

        Assert.True(ok, error);
        Assert.NotEqual(Guid.Empty, normalized!.ContextId);
        Assert.Equal("mes", normalized.Source);
        Assert.Equal("MES-CMD-001", normalized.CommandId);
        Assert.Null(normalized.ExternalBatchRef);
    }

    [Fact]
    public void ProductionContext_RequiresIdempotencyKeyForMes()
    {
        var ok = ManufacturingContextValidator.TryValidate(
            new ProductionContext
            {
                MachineId = "PRESS-01",
                ProductSeries = "LENS-A",
                ProductCode = "LENS-A-01",
                RecipeId = "RCP-A",
                RecipeVersion = "7",
                ToolingInstallationId = Guid.NewGuid(),
                Source = "MES"
            },
            out ProductionContext? _,
            out var error);

        Assert.False(ok);
        Assert.Contains("CommandId", error);
    }
}
