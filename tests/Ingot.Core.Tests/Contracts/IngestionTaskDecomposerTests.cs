// 验证共享契约 IngestionTaskDecomposer 的合法输入、拒绝和兼容边界。

using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class IngestionTaskDecomposerTests
{
    [Fact]
    public void PublishedFirstDeviceCanBecomeReusableTemplateSourceAndBinding()
    {
        var task = Task(AcquisitionProtocols.ModbusTcp) with
        {
            ModbusTcp = new ModbusTcpConnection { Host = "10.0.0.10" }
        };

        Assert.True(IngestionTaskDecomposer.TryCreate(
            task, Model(), "press-template", "press-01-source", out var result, out var errors),
            string.Join("；", errors));

        Assert.Equal("press-template", result!.Template.TemplateId);
        Assert.Equal("press-01-source", result.DataSource.DataSourceId);
        Assert.Equal("10.0.0.10", result.DataSource.ModbusTcp!.Host);
        Assert.Equal("press-template", result.Task.TemplateId);
        Assert.Equal("press-01-source", result.Task.DataSourceId);
        Assert.Equal(task.Version + 1, result.Task.Version);
        Assert.Equal(task.Version + 1, result.Binding.Version);
        Assert.Equal(ConfigurationStatuses.Published, result.Binding.Status);
    }

    [Fact]
    public void MqttActualTopicIsReplacedByStableChannelInTemplate()
    {
        var topic = "plant/press-01/telemetry";
        var task = Task(AcquisitionProtocols.Mqtt) with
        {
            Mqtt = new MqttConnection
            {
                Host = "broker.local",
                Topics = [new MqttTopicSubscription { Channel = "telemetry", Topic = topic }]
            },
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "temperature",
                    SourcePath = "temperature",
                    Topic = topic
                }
            ]
        };

        Assert.True(IngestionTaskDecomposer.TryCreate(
            task, Model(), "press-template", "press-01-source", out var result, out var errors),
            string.Join("；", errors));

        Assert.Equal("telemetry", Assert.Single(result!.Template.ValueMappings).Topic);
        Assert.Equal(topic, Assert.Single(result.Task.ValueMappings).Topic);
    }

    [Fact]
    public void DraftTaskCannotClaimReusableFieldValidation()
    {
        var task = Task(AcquisitionProtocols.ModbusTcp) with { Status = ConfigurationStatuses.Draft };

        Assert.False(IngestionTaskDecomposer.TryCreate(
            task, Model(), "press-template", "press-01-source", out _, out var errors));

        Assert.Contains(errors, item => item.Path == "status");
    }

    [Fact]
    public void MaterializedTaskCannotRewriteItsVersionProvenance()
    {
        var task = Task(AcquisitionProtocols.ModbusTcp) with
        {
            TemplateId = "existing-template",
            TemplateVersion = 2,
            DataSourceId = "existing-source",
            DataSourceVersion = 3
        };

        Assert.False(IngestionTaskDecomposer.TryCreate(
            task, Model(), "replacement", "replacement-source", out _, out var errors));

        Assert.Contains(errors, item => item.Path == "templateId");
    }

    private static IngestionTask Task(string protocol)
        => new()
        {
            TaskId = "press-01",
            Name = "PRESS-01",
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            Protocol = protocol,
            DataModelId = "press-model",
            Source = "connector/press-01",
            SubjectId = "PRESS-01",
            TimestampMode = "edge-received",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "temperature",
                    SourcePath = "holding-register:100:int16",
                    ModbusArea = "holding-register",
                    ModbusAddress = 100,
                    SourceDataType = "int16"
                }
            ]
        };

    private static ProcessDataModel Model()
        => new()
        {
            ModelId = "press-model",
            Name = "Press model",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "temperature",
                        DisplayName = "Temperature",
                        DataType = "int16",
                        Nullable = false
                    }
                ]
            }
        };
}
