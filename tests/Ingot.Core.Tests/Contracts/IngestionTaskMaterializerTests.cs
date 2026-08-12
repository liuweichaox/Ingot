using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class IngestionTaskMaterializerTests
{
    [Fact]
    public void SamePublishedTemplateCreatesIndependentTasksWithoutCopyingMappings()
    {
        var template = Template();
        var firstSource = Source("press-source-01", "PRESS-01", "10.0.0.11");
        var secondSource = Source("press-source-02", "PRESS-02", "10.0.0.12");
        var first = Binding("press-01", firstSource.DataSourceId);
        var second = Binding("press-02", secondSource.DataSourceId);

        Assert.True(IngestionTaskMaterializer.TryCreate(
            template, firstSource, first, null, out var firstTask, out var firstErrors), string.Join("；", firstErrors));
        Assert.True(IngestionTaskMaterializer.TryCreate(
            template, secondSource, second, null, out var secondTask, out var secondErrors), string.Join("；", secondErrors));

        Assert.Equal(firstTask!.ValueMappings, secondTask!.ValueMappings);
        Assert.Equal("mold.temperature", Assert.Single(secondTask.ValueMappings).DataItemCode);
        Assert.Equal("10.0.0.11", firstTask.ModbusTcp!.Host);
        Assert.Equal("10.0.0.12", secondTask.ModbusTcp!.Host);
        Assert.Equal("press-model", secondTask.TemplateId);
        Assert.Equal(3, secondTask.TemplateVersion);
        Assert.Equal(secondSource.DataSourceId, secondTask.DataSourceId);
        Assert.NotEqual(firstTask.TaskId, secondTask.TaskId);
    }

    [Fact]
    public void DraftTemplateCannotCreateADeviceTask()
    {
        var template = Template() with { Status = ConfigurationStatuses.Draft };

        Assert.False(IngestionTaskMaterializer.TryCreate(
            template,
            Source("press-source-01", "PRESS-01", "10.0.0.11"),
            Binding("press-01", "press-source-01"),
            null,
            out _,
            out var errors));
        Assert.Contains(errors, item => item.Path == "template.status");
    }

    [Fact]
    public void MqttTemplateChannelResolvesToEachSourcesActualTopic()
    {
        var template = new IngestionTaskTemplate
        {
            TemplateId = "mqtt-model",
            Name = "MQTT model",
            Status = ConfigurationStatuses.Published,
            Protocol = AcquisitionProtocols.Mqtt,
            DataModelId = "press-data",
            TimestampMode = "edge-received",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "mold.temperature",
                    SourcePath = "temperature",
                    Topic = "telemetry"
                }
            ]
        };
        var source = new DataSourceInstance
        {
            DataSourceId = "mqtt-source",
            Name = "PRESS-09",
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.Mqtt,
            SourceKey = "connector/mqtt/PRESS-09",
            SubjectId = "PRESS-09",
            Mqtt = new MqttConnection
            {
                Host = "broker.local",
                Topics =
                [
                    new MqttTopicSubscription
                    {
                        Channel = "telemetry",
                        Topic = "plant/press-09/telemetry"
                    }
                ]
            }
        };
        var binding = new IngestionTaskBinding
        {
            TaskId = "mqtt-press-09",
            Name = "PRESS-09",
            TemplateId = "mqtt-model",
            DataSourceId = "mqtt-source"
        };

        Assert.True(IngestionTaskMaterializer.TryCreate(
            template, source, binding, null, out var task, out var errors), string.Join("；", errors));
        Assert.Equal("plant/press-09/telemetry", Assert.Single(task!.ValueMappings).Topic);
    }

    private static IngestionTaskTemplate Template()
        => new()
        {
            TemplateId = "press-model",
            Version = 3,
            Name = "Press model",
            Status = ConfigurationStatuses.Published,
            Protocol = AcquisitionProtocols.ModbusTcp,
            DataModelId = "press-data",
            TimestampMode = "edge-received",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "mold.temperature",
                    SourcePath = "holding-register:100:int16",
                    ModbusArea = "holding-register",
                    ModbusAddress = 100,
                    SourceDataType = "int16"
                }
            ]
        };

    private static DataSourceInstance Source(string dataSourceId, string subjectId, string host)
        => new()
        {
            DataSourceId = dataSourceId,
            Name = subjectId,
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.ModbusTcp,
            SourceKey = $"connector/modbus/{subjectId}",
            SubjectId = subjectId,
            ModbusTcp = new ModbusTcpConnection { Host = host }
        };

    private static IngestionTaskBinding Binding(string taskId, string dataSourceId)
        => new()
        {
            TaskId = taskId,
            Name = taskId,
            TemplateId = "press-model",
            TemplateVersion = 3,
            DataSourceId = dataSourceId
        };
}
