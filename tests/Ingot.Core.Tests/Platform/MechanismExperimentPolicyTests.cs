// 验证平台组件 MechanismExperimentPolicy 的成功、拒绝和安全边界。

using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MechanismExperimentPolicyTests
{
    [Fact]
    public void ActiveMechanismFeature_BecomesAuditableAffineOptimizerInput()
    {
        var project = Project();
        var model = new MechanismModelVersion
        {
            ModelId = "thermal-index",
            Version = 2,
            Name = "热压指数",
            Status = MechanismModelStatuses.Active,
            Inputs =
            [
                new MechanismVariableDefinition { Code = "temperature", Unit = "Cel" },
                new MechanismVariableDefinition { Code = "pressure", Unit = "bar" }
            ],
            Output = new MechanismVariableDefinition
            { Code = "thermal.index", Unit = "1", ValidMinimum = 0, ValidMaximum = 1000 },
            Intercept = 5,
            Coefficients = new Dictionary<string, double>
            { ["temperature"] = 2, ["pressure"] = -3 },
            ApplicabilityContext = new Dictionary<string, string> { ["process"] = "molding" },
            ScientificBasis = "受控实验拟合并独立验证。",
            ContentHash = new string('a', 64)
        };
        var fusion = new MechanismFusionDefinition
        {
            FusionId = "thermal-feature",
            Version = 3,
            Name = "热压机理特征",
            Status = MechanismModelStatuses.Active,
            Mode = MechanismFusionModes.MechanismAsFeature,
            MechanismModelId = model.ModelId,
            MechanismModelVersion = model.Version,
            MechanismFeatureCode = "mechanism.thermal-index",
            OutputCode = "quality",
            ApplicabilityContext = new Dictionary<string, string> { ["process"] = "molding" },
            ContentHash = new string('b', 64)
        };

        var applied = MechanismModelExperimentPolicy.Select(project, [model], [fusion]);
        var feature = Assert.Single(applied.DerivedFeatures);

        Assert.Equal("affine", feature.Operator);
        Assert.Equal(["temperature", "pressure"], feature.Inputs);
        Assert.Equal([2d, -3d], feature.Coefficients);
        Assert.Equal(5, feature.Intercept);
        Assert.Equal(model.ContentHash, Assert.Single(applied.References).MechanismModelHash);
        Assert.NotEqual("none", applied.SnapshotHash);
    }

    [Fact]
    public void ForbiddenCombination_IsSentToOptimizerAndValidatedAgainOnReturn()
    {
        var project = Project();
        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Version = 1,
            Status = MechanismClaimStatuses.Active,
            Name = "联合禁区",
            MechanismType = "constraint",
            Statement = "高温高压联合状态不可用。",
            FalsificationCondition = "独立安全试验证明联合状态安全。",
            Applicability = [new MechanismClaimApplicability { DimensionCode = "process", DimensionValue = "molding" }],
            ForbiddenCombinations =
            [
                new MechanismForbiddenCombination
                {
                    Name = "高温高压",
                    Factors =
                    [
                        new MechanismForbiddenCombinationFactor { VariableCode = "temperature", Minimum = 180, Unit = "Cel" },
                        new MechanismForbiddenCombinationFactor { VariableCode = "pressure", Minimum = 8, Unit = "bar" }
                    ]
                }
            ],
            ContentHash = new string('c', 64)
        };
        var knowledge = MechanismKnowledgeExperimentPolicy.Select(project, [claim], []);
        var campaign = MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
            ResearchExperimentOptimizer.BuildCampaign(
                project, ResearchOptimizationIntents.ReachSpecification, null), knowledge);

        Assert.Equal("高温高压", Assert.Single(campaign.ForbiddenCombinations).Name);
        Assert.Throws<ProcessResearchRuleException>(() =>
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(
                new OptimizerSuggestionOutput
                {
                    RecommendedParameters = new Dictionary<string, double>
                    { ["temperature"] = 190, ["pressure"] = 9 }
                }, knowledge));
    }

    private static ResearchProject Project() => new()
    {
        ProjectId = Guid.CreateVersion7(),
        Code = "mechanism-policy",
        Name = "机理策略测试",
        ProcessName = "molding",
        Variables =
        [
            new ResearchVariable { Code = "temperature", Name = "温度", Role = ResearchVariableRoles.Control, Unit = "Cel", LowerLimit = 100, UpperLimit = 200 },
            new ResearchVariable { Code = "pressure", Name = "压力", Role = ResearchVariableRoles.Control, Unit = "bar", LowerLimit = 1, UpperLimit = 10 }
        ],
        Objectives =
        [
            new ResearchObjective { Code = "quality", Name = "质量", Direction = "maximize", Target = 0.9, Unit = "1" }
        ]
    };
}
