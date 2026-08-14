using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

/// <summary>
///     A deliberately non-optical, continuous-process sandbox. This is an
///     engineering coupling test, not evidence of cross-industry validity.
/// </summary>
public sealed class ScenarioNeutralContractTests
{
    [Fact]
    public void ContinuousCoatingScenario_UsesTheSameVersionedContextAdmissionContract()
    {
        var scenario = ContinuousCoatingScenario();

        Assert.True(
            ScenarioPackageValidator.TryValidate(scenario, out var normalized, out var error),
            error);
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["context_capture_status"] = "resolved",
            ["equipment_id"] = "COATER-LINE-02",
            ["execution_id"] = "ROLL-20260814-001",
            ["recipe_version"] = "12",
            ["substrate_lot"] = "FILM-240814-A",
            ["coating_head_id"] = "HEAD-B"
        };

        var admitted = new ResearchContextAdmissionEvaluator().Evaluate(context, normalized);

        Assert.True(admitted.Admitted);
        Assert.Empty(admitted.ExclusionReasons);
        Assert.Contains(normalized!.IngestionTasks, item => item.Id == "http-line-events");
        Assert.DoesNotContain("optical", normalized.PackageId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plc", normalized.IngestionTasks[0].Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContinuousCoatingScenario_FailsClosedWithoutImmutableExecutionIdentity()
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["context_capture_status"] = "resolved",
            ["equipment_id"] = "COATER-LINE-02",
            ["recipe_version"] = "12",
            ["substrate_lot"] = "FILM-240814-A",
            ["coating_head_id"] = "HEAD-B"
        };

        var result = new ResearchContextAdmissionEvaluator().Evaluate(
            context, ContinuousCoatingScenario());

        Assert.False(result.Admitted);
        Assert.Contains(result.ExclusionReasons, reason => reason.Contains("execution_id"));
    }

    private static ScenarioPackage ContinuousCoatingScenario() => new()
    {
        PackageId = "continuous-coating-sandbox",
        Version = 1,
        Name = "连续涂布合成沙盘",
        Description = "通过 HTTP 事件和文件检验结果验证核心上下文契约的场景中立性。",
        Status = ConfigurationStatuses.Published,
        DataModelId = "continuous-web-process",
        DataModelVersion = 1,
        AnalysisPlanId = "coating-thickness-analysis",
        AnalysisPlanVersion = 1,
        IngestionTasks =
        [
            new VersionedConfigurationReference { Id = "http-line-events", Version = 1 },
            new VersionedConfigurationReference { Id = "file-thickness-inspection", Version = 1 }
        ],
        ContextFields =
        [
            Required("equipment_id"),
            Required("execution_id"),
            Required("recipe_version"),
            Required("substrate_lot"),
            Required("coating_head_id")
        ],
        Constraints =
        [
            new ScenarioConstraintDefinition
            {
                Code = "web_tension",
                Name = "卷材张力安全范围",
                Unit = "N",
                Minimum = 18,
                Maximum = 42
            }
        ],
        Terminology = new Dictionary<string, string>
        {
            ["execution"] = "卷次",
            ["quality_outcome"] = "涂层厚度均匀性"
        }
    };

    private static ScenarioContextFieldPolicy Required(string code) => new()
    {
        FieldCode = code,
        Name = code,
        Mode = ScenarioContextModes.RequiredForAnalysis,
        MinimumCoverage = 1
    };
}
