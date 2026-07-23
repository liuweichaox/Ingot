using System.Text.Json;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpPollingSnapshotMapperTests
{
    [Fact]
    public void Map_UsesConfiguredFieldsAndEmitsRecipeOnlyWhenChanged()
    {
        using var document = JsonDocument.Parse("""
            {
              "timestamp": "2026-07-22T10:00:00Z",
              "sequence": 42,
              "productSeries": "SHAFT-20",
              "activeRecipe": {
                "id": "HT-860",
                "version": 3,
                "name": "标准工艺",
                "parameters": { "目标温度℃": 860, "保护气启用": true }
              },
              "sensors": {
                "炉温℃": 852.5,
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
            first.RecipeIdentity);

        Assert.Equal("HT-860@3", first.RecipeIdentity);
        Assert.NotNull(first.RecipeApplied);
        Assert.Null(second.RecipeApplied);
        Assert.IsType<long>(first.RecipeApplied.Data["recipeVersion"]);
        var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            first.RecipeApplied.Data["resolvedParameters"]);
        Assert.IsType<double>(parameters["temperature.target"]);
        Assert.IsType<bool>(parameters["protective_gas.enabled"]);
        Assert.Equal("SHAFT-20", first.Sample.Context["product_series"]);
        Assert.Equal("HT-860", first.Sample.Context["recipe_id"]);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(first.Sample.Data["values"]);
        Assert.IsType<double>(values["furnace.temperature"]);
        Assert.IsType<long>(values["fan.speed"]);
        Assert.IsType<bool>(values["heater.enabled"]);
        Assert.IsType<string>(values["operation.mode"]);
    }

    [Fact]
    public void Map_RejectsMissingRequiredSensor()
    {
        using var document = JsonDocument.Parse("""
            {
              "timestamp": "2026-07-22T10:00:00Z",
              "sequence": 42,
              "productSeries": "SHAFT-20",
              "activeRecipe": {
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

        Assert.Contains("sensors.炉温℃", error.Message, StringComparison.Ordinal);
    }

    private static HttpPollingAcquisitionOptions Options() => new()
    {
        Enabled = true,
        DeviceBaseUrl = "http://127.0.0.1:8100",
        SubjectId = "FURNACE-001",
        ContextFields =
        [
            new ContextFieldMapping { SourcePath = "productSeries", Key = "product_series", Required = true }
        ],
        Fields =
        [
            new ValueFieldMapping { SourcePath = "sensors.炉温℃", Code = "furnace.temperature" },
            new ValueFieldMapping { SourcePath = "sensors.风机转速rpm", Code = "fan.speed", DataType = "integer" },
            new ValueFieldMapping { SourcePath = "sensors.加热器开启", Code = "heater.enabled", DataType = "boolean" },
            new ValueFieldMapping { SourcePath = "sensors.运行模式", Code = "operation.mode", DataType = "string" }
        ],
        Recipe = new RecipeFieldMapping
        {
            IdPath = "activeRecipe.id",
            VersionPath = "activeRecipe.version",
            NamePath = "activeRecipe.name",
            ParametersPath = "activeRecipe.parameters",
            ParameterFields =
            [
                new ValueFieldMapping { SourcePath = "目标温度℃", Code = "temperature.target" },
                new ValueFieldMapping { SourcePath = "保护气启用", Code = "protective_gas.enabled", DataType = "boolean" }
            ]
        }
    };
}
