using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchContextAdmissionEvaluatorTests
{
    private readonly ResearchContextAdmissionEvaluator _evaluator = new();

    [Fact]
    public void ConfigurationMissing_ShouldFailEvenWithoutScenarioPolicy()
    {
        var result = _evaluator.Evaluate(
            new Dictionary<string, string>
            {
                ["context_capture_status"] = "configuration_missing",
                ["equipment_id"] = "PRESS-01",
                ["operation_run_id"] = "RUN-01"
            },
            null);

        Assert.False(result.Admitted);
        Assert.Contains(result.ExclusionReasons, reason => reason.Contains("生产上下文未解析"));
    }

    [Fact]
    public void ResolvedContext_WithAllRequiredFields_ShouldPass()
    {
        var result = _evaluator.Evaluate(CompleteContext("resolved"), OpticalScenario());

        Assert.True(result.Admitted);
        Assert.Empty(result.ExclusionReasons);
    }

    [Fact]
    public void SourceProvidedContext_MissingMold_ShouldFailClosed()
    {
        var context = CompleteContext("source_provided");
        context.Remove("mold_id");

        var result = _evaluator.Evaluate(context, OpticalScenario());

        Assert.False(result.Admitted);
        Assert.Contains(result.ExclusionReasons, reason => reason.Contains("mold_id"));
    }

    [Fact]
    public void SourceProvidedContext_WithAllRequiredFields_ShouldPass()
    {
        var result = _evaluator.Evaluate(CompleteContext("source_provided"), OpticalScenario());

        Assert.True(result.Admitted);
    }

    [Fact]
    public void PolicyHash_ShouldIgnoreContextFieldOrdering()
    {
        var first = OpticalScenario();
        var second = first with { ContextFields = first.ContextFields.Reverse().ToArray() };

        Assert.Equal(
            ResearchContextAdmissionEvaluator.ComputePolicyHash(first),
            ResearchContextAdmissionEvaluator.ComputePolicyHash(second));
    }

    private static Dictionary<string, string> CompleteContext(string captureStatus) => new(StringComparer.Ordinal)
    {
        ["context_capture_status"] = captureStatus,
        ["equipment_id"] = "PRESS-01",
        ["operation_run_id"] = "RUN-01",
        ["recipe_id"] = "LENS-A",
        ["recipe_version"] = "3",
        ["mold_id"] = "MOLD-07",
        ["tooling_installation_id"] = Guid.NewGuid().ToString("D"),
        ["material_lot_ref"] = "LOT-09",
        ["product_series"] = "LENS"
    };

    internal static ScenarioPackage OpticalScenario() => new()
    {
        PackageId = "optical-molding",
        Version = 2,
        Name = "光学模压",
        Status = ConfigurationStatuses.Published,
        DataModelId = "optical",
        AnalysisPlanId = "optical-analysis",
        ContextFields =
        [
            Required("equipment_id"),
            Required("operation_run_id"),
            Required("recipe_id"),
            Required("recipe_version"),
            Required("mold_id"),
            Required("tooling_installation_id"),
            Required("material_lot_ref"),
            Required("product_series"),
            new ScenarioContextFieldPolicy
            {
                FieldCode = "maintenance_status",
                Name = "维护状态",
                Mode = ScenarioContextModes.RecordWhenAvailable
            }
        ]
    };

    private static ScenarioContextFieldPolicy Required(string code) => new()
    {
        FieldCode = code,
        Name = code,
        Mode = ScenarioContextModes.RequiredForAnalysis,
        MinimumCoverage = 1
    };
}
