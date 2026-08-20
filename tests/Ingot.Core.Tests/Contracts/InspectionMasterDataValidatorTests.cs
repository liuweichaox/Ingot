// 验证共享契约 InspectionMasterDataValidator 的合法输入、拒绝和兼容边界。

using Ingot.Contracts.Inspections;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class InspectionMasterDataValidatorTests
{
    [Fact]
    public void InspectionDefinition_NormalizesCharacteristicLimitsAndInputType()
    {
        var ok = InspectionMasterDataValidator.TryValidate(
            new InspectionDefinition
            {
                Code = "surface.form",
                Name = "Surface Form",
                Characteristics =
                [
                    new InspectionCharacteristicDefinition
                    {
                        Code = "pv",
                        Name = "PV",
                        InputType = "NUMERIC",
                        Unit = "um",
                        LowerLimit = 0,
                        UpperLimit = 2
                    }
                ]
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("numeric", normalized!.Characteristics[0].InputType);
        Assert.Equal("surface.form", normalized.Code);
    }

    [Fact]
    public void InspectionDefinition_RequiresAndNormalizesSelectOptions()
    {
        var ok = InspectionMasterDataValidator.TryValidate(
            new InspectionDefinition
            {
                Code = "surface.appearance",
                Name = "外观检查",
                Characteristics =
                [
                    new InspectionCharacteristicDefinition
                    {
                        Code = "defect",
                        Name = "缺陷类型",
                        InputType = "select",
                        AllowedValues = [" 合格 ", "划伤", "划伤"],
                        PassingValues = [" 合格 "]
                    }
                ]
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(["合格", "划伤"], normalized!.Characteristics[0].AllowedValues);
        Assert.Equal(["合格"], normalized.Characteristics[0].PassingValues);
    }

    [Fact]
    public void InspectionDefinition_RejectsSelectWithoutServerSidePassingValues()
    {
        var ok = InspectionMasterDataValidator.TryValidate(
            new InspectionDefinition
            {
                Code = "surface.appearance",
                Name = "外观检查",
                Characteristics =
                [
                    new InspectionCharacteristicDefinition
                    {
                        Code = "defect",
                        Name = "缺陷类型",
                        InputType = "select",
                        AllowedValues = ["合格", "划伤"]
                    }
                ]
            },
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("合格值", error, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectionCharacteristicOutcome_IsDerivedFromDefinition()
    {
        var definition = new InspectionCharacteristicDefinition
        {
            Code = "defect",
            Name = "缺陷类型",
            InputType = "select",
            AllowedValues = ["合格", "划伤"],
            PassingValues = ["合格"]
        };

        Assert.Equal("PASS", InspectionCharacteristicOutcomeEvaluator.Evaluate(definition, "合格"));
        Assert.Equal("FAIL", InspectionCharacteristicOutcomeEvaluator.Evaluate(definition, "划伤"));
    }

    [Fact]
    public void FeatureDefinition_DefaultsBoundaryModeByAggregation()
    {
        var ok = InspectionMasterDataValidator.TryValidate(
            new FeatureDefinition
            {
                Code = "anneal.rate_c_per_min",
                Name = "Anneal Rate",
                PhaseCode = "anneal",
                Signal = "mold.temperature_c",
                Aggregation = "slope"
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("include_leading", normalized!.BoundaryMode);
    }

    [Fact]
    public void PhaseMapping_DerivesStableMappingId()
    {
        var ok = InspectionMasterDataValidator.TryValidate(
            new PhaseMapping
            {
                MappingId = "",
                ProcessSpecificationId = "RCP-1",
                ProcessSpecification = "3",
                ProcessTemplate = "optical",
                ProcessStep = "4",
                PhaseCode = "anneal"
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("rcp-1:3:optical:4", normalized!.MappingId);
    }
}
