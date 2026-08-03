using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class ScenarioPackageValidatorTests
{
    [Fact]
    public void ValidPackage_NormalizesVersionedScenarioPolicy()
    {
        var value = Package() with
        {
            PackageId = "  FIRST-SCENARIO ",
            ContextFields =
            [
                new ScenarioContextFieldPolicy
                {
                    FieldCode = " Material_Lot ",
                    Name = "材料批次",
                    Mode = "REQUIRED-FOR-ANALYSIS",
                    MinimumCoverage = 0.9,
                    MinimumFactorOverlap = 0.5
                }
            ]
        };

        var valid = ScenarioPackageValidator.TryValidate(value, out var normalized, out var error);

        Assert.True(valid, error);
        Assert.Equal("first-scenario", normalized!.PackageId);
        Assert.Equal("material_lot", normalized.ContextFields[0].FieldCode);
        Assert.Equal(ScenarioContextModes.RequiredForAnalysis, normalized.ContextFields[0].Mode);
    }

    [Fact]
    public void AnalysisFieldWithoutCoverage_IsRejected()
    {
        var value = Package() with
        {
            ContextFields =
            [
                new ScenarioContextFieldPolicy
                {
                    FieldCode = "tooling_revision",
                    Name = "工装版本",
                    Mode = ScenarioContextModes.ValidatedForModeling
                }
            ]
        };

        Assert.False(ScenarioPackageValidator.TryValidate(value, out _, out var error));
        Assert.Contains("最低覆盖率", error);
    }

    [Fact]
    public void DuplicateReferencesAndInvalidConstraintBounds_AreRejected()
    {
        var duplicate = Package() with
        {
            AcquisitionProfiles =
            [
                new VersionedConfigurationReference { Id = "device-a", Version = 1 },
                new VersionedConfigurationReference { Id = "DEVICE-A", Version = 1 }
            ]
        };
        Assert.False(ScenarioPackageValidator.TryValidate(duplicate, out _, out var duplicateError));
        Assert.Contains("重复", duplicateError);

        var invalidBounds = Package() with
        {
            Constraints =
            [
                new ScenarioConstraintDefinition
                {
                    Code = "temperature",
                    Name = "温度安全范围",
                    Minimum = 200,
                    Maximum = 100
                }
            ]
        };
        Assert.False(ScenarioPackageValidator.TryValidate(invalidBounds, out _, out var boundsError));
        Assert.Contains("下限不能大于上限", boundsError);
    }

    private static ScenarioPackage Package()
        => new()
        {
            PackageId = "first-scenario",
            Version = 1,
            Name = "首个场景",
            Status = ConfigurationStatuses.Published,
            DataModelId = "model-a",
            DataModelVersion = 1,
            AnalysisPlanId = "analysis-a",
            AnalysisPlanVersion = 1
        };
}
