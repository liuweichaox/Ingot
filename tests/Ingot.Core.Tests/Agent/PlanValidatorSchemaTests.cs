using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class PlanValidatorSchemaTests
{
    private readonly DefaultPlanValidator validator = new(
        Options.Create(new ChatOptions { MaxToolCalls = 8 }));

    [Fact]
    public void Validator_ParsesAllSupportedSchemaTypesFromStringArguments()
    {
        var tool = new SchemaTool();
        var plan = Plan(new Dictionary<string, string?>
        {
            ["text"] = "ok",
            ["count"] = "8",
            ["ratio"] = "0.25",
            ["enabled"] = "true"
        });

        Assert.True(validator.TryValidate(
            ProductEntryPoints.Chat,
            plan,
            new Dictionary<string, IAnalysisTool> { [tool.Definition.Name] = tool },
            out var error), error);
    }

    [Theory]
    [InlineData("count", "8.5")]
    [InlineData("count", " 8")]
    [InlineData("count", "08")]
    [InlineData("count", "21")]
    [InlineData("ratio", "NaN")]
    [InlineData("ratio", "1.5")]
    [InlineData("ratio", "+0.25")]
    [InlineData("enabled", "yes")]
    [InlineData("enabled", "TRUE")]
    [InlineData("text", "")]
    public void Validator_RejectsInvalidOrOutOfRangeTypedValues(string key, string value)
    {
        var tool = new SchemaTool();
        var arguments = new Dictionary<string, string?>
        {
            ["text"] = "ok", ["count"] = "8", ["ratio"] = "0.25", ["enabled"] = "true"
        };
        arguments[key] = value;

        Assert.False(validator.TryValidate(
            ProductEntryPoints.Chat,
            Plan(arguments),
            new Dictionary<string, IAnalysisTool> { [tool.Definition.Name] = tool },
            out _));
    }

    [Fact]
    public void Validator_RejectsExplicitNullForEverySupportedType()
    {
        var tool = new SchemaTool();
        foreach (var key in new[] { "text", "count", "ratio", "enabled" })
        {
            var arguments = new Dictionary<string, string?>
            {
                ["text"] = "ok", ["count"] = "8", ["ratio"] = "0.25", ["enabled"] = "true"
            };
            arguments[key] = null;
            Assert.False(validator.TryValidate(
                ProductEntryPoints.Chat,
                Plan(arguments),
                new Dictionary<string, IAnalysisTool> { [tool.Definition.Name] = tool },
                out _));
        }
    }

    private static AnalysisPlan Plan(IReadOnlyDictionary<string, string?> arguments) => new()
    {
        EntryPoint = ProductEntryPoints.Chat,
        Intent = "schema-test",
        Summary = "schema-test",
        ToolCalls = [new AnalysisToolCall { Tool = "schema_tool", Arguments = arguments }]
    };

    private sealed class SchemaTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "schema_tool",
            Version = "v1",
            Description = "schema test",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                required = new[] { "text", "count", "ratio", "enabled" },
                properties = new
                {
                    text = new { type = "string", minLength = 1, maxLength = 10 },
                    count = new { type = "integer", minimum = 1, maximum = 20 },
                    ratio = new { type = "number", minimum = 0, maximum = 1 },
                    enabled = new { type = "boolean" }
                },
                additionalProperties = false
            })
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
