// 验证共享契约 ProcessConfigurationValidator 的合法输入、拒绝和兼容边界。

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
                        DisplayName = "上模温度℃",
                        Unit = "Cel"
                    }
                ]
            },
            ControlParameters =
            [
                new ControlParameterDefinition
                {
                    Code = "Upper_Mold.Set_Temperature",
                    DisplayName = "上模设置温度℃",
                    Unit = "Cel"
                }
            ]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal("optical-molding.demo", normalized!.ModelId);
        Assert.Equal("upper_mold.temperature", normalized.Acquisition.DataItems[0].Code);
        Assert.Equal("upper_mold.set_temperature", normalized.ControlParameters[0].Code);
    }

    [Fact]
    public void DataModel_AcceptsOneIntegerStageNumberDataItem()
    {
        var value = DataModel() with
        {
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition { Code = "temperature", DisplayName = "温度" },
                    new ProcessDataItemDefinition
                    {
                        Code = "process.stage_number",
                        DisplayName = "阶段号",
                        DataType = "integer",
                        Category = "stage",
                        Nullable = false
                    }
                ]
            }
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal("process.stage_number", normalized!.Acquisition.DataItems[1].Code);
    }

    [Fact]
    public void DataModel_RejectsNonIntegerStageNumber()
    {
        var value = DataModel() with
        {
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "process.stage_number",
                        DisplayName = "阶段号",
                        DataType = "double",
                        Category = "stage"
                    }
                ]
            }
        };

        Assert.False(ProcessConfigurationValidator.TryValidate(value, out _, out var error));
        Assert.Contains("整数类型", error);
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
                    new ProcessDataItemDefinition { Code = "press.load", DisplayName = "压力1" },
                    new ProcessDataItemDefinition { Code = "PRESS.LOAD", DisplayName = "压力2" }
                ]
            }
        };

        Assert.False(ProcessConfigurationValidator.TryValidate(value, out _, out var error));
        Assert.Contains("重复", error);
    }

    [Fact]
    public void ProcessSpecification_AcceptsTypedValuesWithoutChangeReason()
    {
        using var document = JsonDocument.Parse("128.5");
        var value = new ProcessSpecification
        {
            ProcessSpecificationId = "RCP-LENS-A",
            Version = 7,
            BasedOnVersion = 6,
            Name = "镜片 A 工艺规范",
            DataModelId = "optical-molding.demo",
            Values =
            [
                new ControlParameterValue { Code = "work.set_pressure", Value = document.RootElement.Clone() }
            ]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        Assert.Equal("rcp-lens-a", normalized!.ProcessSpecificationId);
        Assert.Equal(128.5, normalized.Values[0].Value.GetDouble());
        Assert.DoesNotContain("reason", JsonSerializer.Serialize(normalized), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisPlan_RequiresAtLeastOneSignal()
    {
        var value = new ProcessAnalysisPlan
        {
            PlanId = "execution-comparison",
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

    [Fact]
    public void AnalysisPlan_NormalizesKnownUnmeasuredConfounders()
    {
        var value = new ProcessAnalysisPlan
        {
            PlanId = "window-comparison",
            Name = "连续过程窗口对比",
            DataModelId = "process-model",
            Signals = [new AnalysisSignalSelection { DataItemCode = "temperature" }],
            KnownUnmeasuredConfounders =
            [
                new()
                {
                    Code = " Operator_Experience ",
                    Name = " 操作员经验 ",
                    Description = " 尚未进入数据链 "
                }
            ]
        };

        Assert.True(ProcessConfigurationValidator.TryValidate(value, out var normalized, out var error), error);
        var confounder = Assert.Single(normalized!.KnownUnmeasuredConfounders);
        Assert.Equal("operator_experience", confounder.Code);
        Assert.Equal("操作员经验", confounder.Name);
        Assert.Equal("尚未进入数据链", confounder.Description);
    }

    [Fact]
    public void AnalysisPlan_RejectsDuplicateKnownUnmeasuredConfounderCodes()
    {
        var value = new ProcessAnalysisPlan
        {
            PlanId = "window-comparison",
            Name = "连续过程窗口对比",
            DataModelId = "process-model",
            Signals = [new AnalysisSignalSelection { DataItemCode = "temperature" }],
            KnownUnmeasuredConfounders =
            [
                new() { Code = "operator", Name = "操作员" },
                new() { Code = " OPERATOR ", Name = "值班人员" }
            ]
        };

        Assert.False(ProcessConfigurationValidator.TryValidate(value, out _, out var error));
        Assert.Contains("重复", error);
    }

    private static ProcessDataModel DataModel() => new()
    {
        ModelId = "model",
        Name = "模型",
        Acquisition = new AcquisitionModel
        {
            DataItems = [new ProcessDataItemDefinition { Code = "signal", DisplayName = "信号" }]
        }
    };
}
