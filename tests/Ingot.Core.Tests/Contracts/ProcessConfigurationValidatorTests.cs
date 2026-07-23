using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class ProcessConfigurationValidatorTests
{
    [Fact]
    public void DataModel_NormalizesStableCodesAndKeepsDefinitionsSeparateFromValues()
    {
        var value = new ProcessDataModel
        {
            ModelId = " Optical-Molding.Demo ",
            Name = "光学模压",
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = " Upper_Mold.Temperature ",
                        SourceField = "上模温度℃",
                        Unit = "Cel"
                    }
                ]
            },
            RecipeParameters =
            [
                new RecipeParameterDefinition
                {
                    Code = "Upper_Mold.Set_Temperature",
                    SourceField = "上模设置温度℃",
                    Unit = "Cel"
                }
            ],
            Stages =
            [
                new ProcessStageDefinition { SourceStep = "10", Code = "Preheat", Name = "预热" }
            ]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal("optical-molding.demo", normalized!.ModelId);
        Assert.Equal("upper_mold.temperature", normalized.Acquisition.DataItems[0].Code);
        Assert.Equal("upper_mold.set_temperature", normalized.RecipeParameters[0].Code);
    }

    [Fact]
    public void DataModel_RejectsDuplicateDataItems()
    {
        var value = DataModel() with
        {
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition { Code = "press.load", SourceField = "压力1" },
                    new ProcessDataItemDefinition { Code = "PRESS.LOAD", SourceField = "压力2" }
                ]
            }
        };

        Assert.False(ProcessConfigurationValidator.TryValidate(value, out _, out var error));
        Assert.Contains("重复", error);
    }

    [Fact]
    public void RecipeVersion_AcceptsTypedValuesWithoutChangeReason()
    {
        using var document = JsonDocument.Parse("128.5");
        var value = new RecipeVersion
        {
            RecipeId = "RCP-LENS-A",
            Version = 7,
            BasedOnVersion = 6,
            Name = "镜片 A 配方",
            DataModelId = "optical-molding.demo",
            Values =
            [
                new RecipeParameterValue { Code = "work.set_pressure", Value = document.RootElement.Clone() }
            ]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal("rcp-lens-a", normalized!.RecipeId);
        Assert.Equal(128.5, normalized.Values[0].Value.GetDouble());
        Assert.DoesNotContain("reason", JsonSerializer.Serialize(normalized), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisPlan_RequiresAtLeastOneSignal()
    {
        var value = new ProcessAnalysisPlan
        {
            PlanId = "cycle-comparison",
            Name = "周期对比",
            DataModelId = "optical-molding.demo"
        };

        Assert.False(ProcessConfigurationValidator.TryValidate(value, out _, out var error));
        Assert.Contains("至少需要一个数据项", error);
    }

    [Fact]
    public void AnalysisPlan_NormalizesConfiguredComparisonKeys()
    {
        var value = new ProcessAnalysisPlan
        {
            PlanId = "window-comparison",
            Name = "连续过程窗口对比",
            DataModelId = "process-model",
            AnalysisScope = "analysis-window",
            ComparisonKeys = [" Material_Grade ", "operation.code"],
            Signals = [new AnalysisSignalSelection { DataItemCode = "temperature" }]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal(["material_grade", "operation.code"], normalized!.ComparisonKeys);
    }

    private static ProcessDataModel DataModel() => new()
    {
        ModelId = "model",
        Name = "模型",
        Acquisition = new AcquisitionModel
        {
            DataItems = [new ProcessDataItemDefinition { Code = "signal", SourceField = "信号" }]
        }
    };
}
