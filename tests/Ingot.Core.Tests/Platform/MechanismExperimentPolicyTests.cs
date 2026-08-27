// 验证平台组件 MechanismExperimentPolicy 的成功、拒绝和安全边界。

using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MechanismExperimentPolicyTests
{
    [Fact]
    public void Selection_UsesOnlyActiveApplicableConflictFreeClaims()
    {
        var project = Project();
        var applicable = Claim(project, MechanismClaimStatuses.Active, "适用且已激活", "process", "molding");
        var draft = Claim(project, MechanismClaimStatuses.Draft, "草稿", "process", "molding");
        var reviewed = Claim(project, MechanismClaimStatuses.Reviewed, "已复核", "process", "molding");
        var mismatched = Claim(project, MechanismClaimStatuses.Active, "范围不匹配", "process", "coating");
        var falsified = Claim(project, MechanismClaimStatuses.Falsified, "已反证", "process", "molding");
        var retired = Claim(project, MechanismClaimStatuses.Retired, "已停用", "process", "molding");
        var conflictedLeft = Claim(project, MechanismClaimStatuses.Active, "冲突左侧", "process", "molding");
        var conflictedRight = Claim(project, MechanismClaimStatuses.Active, "冲突右侧", "process", "molding");
        var conflicts = new[]
        {
            new MechanismClaimConflict
            {
                ConflictId = Guid.CreateVersion7(),
                ProjectId = project.ProjectId,
                LeftClaimId = conflictedLeft.ClaimId,
                LeftClaimVersion = 1,
                RightClaimId = conflictedRight.ClaimId,
                RightClaimVersion = 1,
                ConflictKind = "contradiction",
                Rationale = "同一范围内方向相反。",
                Status = "open"
            }
        };

        var selected = MechanismKnowledgeExperimentPolicy.Select(
            project,
            [applicable, draft, reviewed, mismatched, falsified, retired, conflictedLeft, conflictedRight],
            conflicts);

        var claim = Assert.Single(selected.Claims);
        Assert.Equal(applicable.ClaimId, claim.ClaimId);
    }

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
            ResearchOptimizationService.BuildCampaign(
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

    private static MechanismClaimVersion Claim(
        ResearchProject project,
        string status,
        string name,
        string dimension,
        string value)
        => new()
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Version = 1,
            Status = status,
            Name = name,
            MechanismType = "monotonic",
            Statement = "用于验证状态和适用范围选择规则。",
            FalsificationCondition = "独立实验结果与预期方向相反。",
            Variables =
            [
                new MechanismClaimVariable
                {
                    VariableCode = "temperature",
                    VariableRole = "cause",
                    Direction = "increase",
                    Unit = "Cel"
                }
            ],
            Applicability =
            [
                new MechanismClaimApplicability
                {
                    DimensionCode = dimension,
                    DimensionValue = value
                }
            ],
            ContentHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()))
        };
}
