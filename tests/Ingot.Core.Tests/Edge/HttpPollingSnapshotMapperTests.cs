// 验证边缘组件 HttpPollingSnapshotMapper 的协议、状态和失败边界。

using System.Text.Json;
using Ingot.Edge.ConnectorHost.Acquisition;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpPollingSnapshotMapperTests
{
    [Fact]
    public void Map_UsesConfiguredFieldsAndEmitsProcessSpecificationOnlyWhenChanged()
    {
        using var document = JsonDocument.Parse("""
            {
              "timestamp": "2026-07-22T10:00:00Z",
              "sequence": 42,
              "productFamilyCode": "SHAFT-20",
              "activeProcessSpecification": {
                "id": "HT-860",
                "version": 3,
                "name": "标准工艺",
                "parameters": { "目标温度℃": 860, "保护气启用": true }
              },
              "sensors": {
                "温度℃": 852.5,
                "风机转速rpm": 1450,
                "加热器开启": true,
                "运行模式": "normalizing"
              }
            }
            """);
        var options = Options();

        var first = HttpPollingSnapshotMapper.Map(
            document.RootElement,
            options,
            "edge/EDGE-001/connector/furnace",
            null);
        var second = HttpPollingSnapshotMapper.Map(
            document.RootElement,
            options,
            "edge/EDGE-001/connector/furnace",
            first.ProcessSpecificationIdentity);

        Assert.Equal("HT-860@3", first.ProcessSpecificationIdentity);
        Assert.NotNull(first.ProcessSpecificationApplied);
        Assert.Null(second.ProcessSpecificationApplied);
        Assert.IsType<long>(first.ProcessSpecificationApplied.Data["processSpecificationVersion"]);
        var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            first.ProcessSpecificationApplied.Data["resolvedParameters"]);
        Assert.IsType<double>(parameters["temperature.target"]);
        Assert.IsType<bool>(parameters["protective_gas.enabled"]);
        Assert.Equal("SHAFT-20", first.Sample.Context["product_family_code"]);
        Assert.Equal("HT-860", first.Sample.Context["process_specification_id"]);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(first.Sample.Data["values"]);
        Assert.IsType<double>(values["furnace.temperature"]);
        Assert.IsType<long>(values["fan.speed"]);
        Assert.IsType<bool>(values["heater.enabled"]);
        Assert.IsType<string>(values["operation.mode"]);
        Assert.Equal(
            new AppliedConfigurationRef("ingestion-task", "FURNACE-TASK", 7),
            first.Sample.AppliedConfiguration);
    }

    [Fact]
    public void Map_RejectsMissingRequiredSensor()
    {
        using var document = JsonDocument.Parse("""
            {
              "timestamp": "2026-07-22T10:00:00Z",
              "sequence": 42,
              "productFamilyCode": "SHAFT-20",
              "activeProcessSpecification": {
                "id": "HT-860",
                "version": 3,
                "parameters": {}
              },
              "sensors": {}
            }
            """);

        var error = Assert.Throws<InvalidDataException>(() => HttpPollingSnapshotMapper.Map(
            document.RootElement,
            Options(),
            "edge/EDGE-001/connector/furnace",
            null));

        Assert.Contains("sensors.温度℃", error.Message, StringComparison.Ordinal);
    }

    private static HttpPollingAcquisitionOptions Options() => new()
    {
        ConfigurationKind = "ingestion-task",
        ConfigurationId = "FURNACE-TASK",
        ConfigurationVersion = 7,
        Enabled = true,
        DeviceBaseUrl = "http://127.0.0.1:8100",
        SubjectId = "FURNACE-001",
        ContextFields =
        [
            new ContextFieldMapping { SourcePath = "productFamilyCode", Key = "product_family_code", Required = true }
        ],
        Fields =
        [
            new ValueFieldMapping { SourcePath = "sensors.温度℃", Code = "furnace.temperature" },
            new ValueFieldMapping { SourcePath = "sensors.风机转速rpm", Code = "fan.speed", DataType = "integer" },
            new ValueFieldMapping { SourcePath = "sensors.加热器开启", Code = "heater.enabled", DataType = "boolean" },
            new ValueFieldMapping { SourcePath = "sensors.运行模式", Code = "operation.mode", DataType = "string" }
        ],
        ProcessSpecification = new ProcessSpecificationFieldMapping
        {
            IdPath = "activeProcessSpecification.id",
            VersionPath = "activeProcessSpecification.version",
            NamePath = "activeProcessSpecification.name",
            ParametersPath = "activeProcessSpecification.parameters",
            ParameterFields =
            [
                new ValueFieldMapping { SourcePath = "目标温度℃", Code = "temperature.target" },
                new ValueFieldMapping { SourcePath = "保护气启用", Code = "protective_gas.enabled", DataType = "boolean" }
            ]
        }
    };
}
