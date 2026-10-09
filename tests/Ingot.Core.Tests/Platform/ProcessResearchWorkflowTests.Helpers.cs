// 提供下一配方建议闭环测试使用的建议条件夹具。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected const string TestSiteCode = "SITE-001";
    protected const string TestProcessSpecificationId = "optical-press-spec";

    protected static IReadOnlyList<ResearchVariableSetting> Parameters(double temperature, double force)
        =>
        [
            new ResearchVariableSetting { VariableCode = "holding-temperature", Value = temperature, Unit = "Cel" },
            new ResearchVariableSetting { VariableCode = "press-force", Value = force, Unit = "kN" }
        ];

    protected static RecipeRecommendationBrief BriefDraft()
        => new()
        {
            SiteCode = TestSiteCode,
            ProcessSpecificationId = TestProcessSpecificationId,
            Name = "光学模压配方优化",
            Context = new Dictionary<string, string>
            {
                [RecipeRecommendationScopeKeys.ProductFamilyCode] = "lens-a",
                [RecipeRecommendationScopeKeys.ProductCode] = "product-a",
                [RecipeRecommendationScopeKeys.EquipmentId] = "press-01",
                [RecipeRecommendationScopeKeys.ProcessSpecificationId] = TestProcessSpecificationId
            },
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "form-error", Name = "面形误差", Unit = "um", Direction = "minimize", Target = 0.4
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "holding-temperature", Name = "保压温度", Role = ResearchVariableRoles.Control,
                    Unit = "Cel", LowerLimit = 480, UpperLimit = 550
                },
                new ResearchVariable
                {
                    Code = "press-force", Name = "模压力", Role = ResearchVariableRoles.Control,
                    Unit = "kN", LowerLimit = 5, UpperLimit = 20
                }
            ],
            Constraints =
            [
                new ResearchConstraint
                {
                    Code = "temperature-safety", Description = "保压温度安全上限",
                    VariableCode = "holding-temperature", Operator = "<=", Limit = 545, Unit = "Cel",
                    SafetyCritical = true
                }
            ],
            OptimizationFeatures = new ResearchOptimizationFeatureSet
            {
                FeatureSetId = "declared-test-features",
                Version = 1,
                DerivedFeatures =
                [
                    new ResearchDerivedFeature
                    {
                        Name = "temperature-force-ratio", Operator = ResearchDerivedFeatureOperators.Ratio,
                        Inputs = ["holding-temperature", "press-force"], NormalizationScale = 100
                    }
                ]
            }
        };
}
