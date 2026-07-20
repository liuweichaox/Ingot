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
                RecipeId = "RCP-1",
                RecipeVersion = "3",
                RecipeTemplate = "optical",
                RecipeStep = "4",
                PhaseCode = "anneal"
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("rcp-1:3:optical:4", normalized!.MappingId);
    }
}

