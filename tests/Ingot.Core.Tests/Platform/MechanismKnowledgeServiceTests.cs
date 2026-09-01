// 验证平台组件 MechanismKnowledgeService 的成功、拒绝和安全边界。

using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MechanismKnowledgeServiceTests
{
    [Fact]
    public async Task Draft_RejectsUnknownResearchProject()
    {
        var service = new MechanismKnowledgeService(
            new MemoryMechanismKnowledgeStore(),
            new MissingResearchProjectContextReader());

        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.SaveDraftAsync(
            Guid.CreateVersion7(), Draft(), "engineer"));
    }

    [Fact]
    public async Task Draft_IsStructuredVersionedAndRequiresIndependentReview()
    {
        var store = new MemoryMechanismKnowledgeStore();
        var service = CreateService(store);
        var projectId = Guid.CreateVersion7();

        var draft = await service.SaveDraftAsync(projectId, Draft(), "engineer-a");

        Assert.Equal(1, draft.Version);
        Assert.Equal(MechanismClaimStatuses.Draft, draft.Status);
        Assert.Single(draft.Variables);
        Assert.Single(draft.Applicability);
        Assert.Single(draft.Evidence);
        Assert.Equal(64, draft.ContentHash.Length);
        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.ReviewAsync(
            draft.ClaimId,
            new MechanismClaimReviewRequest("approve", null),
            "engineer-a"));

        var reviewed = await service.ReviewAsync(
            draft.ClaimId,
            new MechanismClaimReviewRequest("approve", "引用、单位和适用范围已核对。"),
            "engineer-b");

        Assert.Equal(MechanismClaimStatuses.Reviewed, reviewed.Status);
        Assert.Equal("engineer-b", reviewed.ReviewedBy);
    }

    [Fact]
    public async Task Draft_RejectsMissingApplicabilityOrEvidence()
    {
        var service = CreateService(new MemoryMechanismKnowledgeStore());
        var projectId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.SaveDraftAsync(
            projectId, Draft() with { Applicability = [] }, "engineer"));
        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.SaveDraftAsync(
            projectId, Draft() with { Evidence = [] }, "engineer"));
    }

    [Fact]
    public async Task Conflict_PreservesBothClaimVersions()
    {
        var store = new MemoryMechanismKnowledgeStore();
        var service = CreateService(store);
        var projectId = Guid.CreateVersion7();
        var left = await service.SaveDraftAsync(projectId, Draft() with { Name = "温度升高改善流动" }, "a");
        var right = await service.SaveDraftAsync(projectId, Draft() with { Name = "温度升高导致降解" }, "b");

        var conflict = await service.AddConflictAsync(projectId, new MechanismClaimConflictRequest
        {
            LeftClaimId = left.ClaimId,
            LeftClaimVersion = left.Version,
            RightClaimId = right.ClaimId,
            RightClaimVersion = right.Version,
            ConflictKind = "scope-overlap",
            Rationale = "两个声明在同一材料和设备范围内方向相反。"
        }, "reviewer");

        Assert.Equal("open", conflict.Status);
        Assert.NotNull(await store.GetClaimAsync(left.ClaimId));
        Assert.NotNull(await store.GetClaimAsync(right.ClaimId));
    }

    [Fact]
    public async Task Conflict_RequiresIndependentResolutionAndThenCloses()
    {
        var store = new MemoryMechanismKnowledgeStore();
        var service = CreateService(store);
        var projectId = Guid.CreateVersion7();
        var left = await service.SaveDraftAsync(projectId, Draft(), "a");
        var right = await service.SaveDraftAsync(projectId, Draft() with { Name = "相反声明" }, "b");
        var conflict = await service.AddConflictAsync(projectId, new MechanismClaimConflictRequest
        {
            LeftClaimId = left.ClaimId,
            RightClaimId = right.ClaimId,
            ConflictKind = "contradiction",
            Rationale = "方向相反。"
        }, "registrar");

        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.ResolveConflictAsync(
            projectId, conflict.ConflictId,
            new MechanismClaimConflictResolutionRequest { Resolution = "限定不同温度区间。" },
            "registrar"));
        var resolved = await service.ResolveConflictAsync(
            projectId, conflict.ConflictId,
            new MechanismClaimConflictResolutionRequest { Resolution = "限定不同温度区间。" },
            "independent-reviewer");

        Assert.Equal("resolved", resolved.Status);
        Assert.Equal("independent-reviewer", resolved.ResolvedBy);
    }

    [Theory]
    [InlineData("unknown", "increase", 0)]
    [InlineData("cause", "sideways", 0)]
    [InlineData("cause", "increase", -1)]
    public async Task Draft_RejectsInvalidVariableSemantics(string role, string direction, long delay)
    {
        var service = CreateService(new MemoryMechanismKnowledgeStore());
        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.SaveDraftAsync(
            Guid.CreateVersion7(), Draft() with
            {
                Variables = [new MechanismClaimVariable
                    { VariableCode = "holding.temperature", VariableRole = role, Direction = direction,
                        DelayMilliseconds = delay, Unit = "Cel" }]
            }, "engineer"));
    }

    [Fact]
    public async Task Lifecycle_RequiresTwoDistinctFormalResultsBeforeActivation()
    {
        var store = new MemoryMechanismKnowledgeStore();
        var service = CreateService(store);
        var projectId = Guid.CreateVersion7();
        var claim = await service.SaveDraftAsync(projectId, Draft(), "creator");
        claim = await service.ReviewAsync(
            claim.ClaimId, new MechanismClaimReviewRequest("approve", "结构正确"), "reviewer");

        var firstResult = Guid.CreateVersion7().ToString();
        claim = await service.TransitionAsync(projectId, claim.ClaimId, new MechanismClaimLifecycleRequest
        {
            TargetStatus = MechanismClaimStatuses.Supported,
            EvidenceKind = "recipe-recommendation-outcome",
            ReferenceId = firstResult,
            ContentHash = new string('b', 64),
            ValidationHypothesisId = Guid.CreateVersion7(),
            EvaluationSummary = "真实运行结果覆盖声明变量并观察到预期方向。"
        }, "validator-a");
        Assert.Equal(MechanismClaimStatuses.Supported, claim.Status);

        await Assert.ThrowsAsync<ResearchAssetRuleException>(() => service.TransitionAsync(
            projectId, claim.ClaimId, new MechanismClaimLifecycleRequest
            {
                TargetStatus = MechanismClaimStatuses.Validated,
                EvidenceKind = "recipe-recommendation-outcome",
                ReferenceId = firstResult,
                ContentHash = new string('b', 64),
                ValidationHypothesisId = Guid.CreateVersion7(),
                EvaluationSummary = "重复引用应被拒绝。"
            }, "validator-b"));

        claim = await service.TransitionAsync(projectId, claim.ClaimId, new MechanismClaimLifecycleRequest
        {
            TargetStatus = MechanismClaimStatuses.Validated,
            EvidenceKind = "recipe-recommendation-outcome",
            ReferenceId = Guid.CreateVersion7().ToString(),
            ContentHash = new string('c', 64),
            ValidationHypothesisId = Guid.CreateVersion7(),
            EvaluationSummary = "独立真实运行再次观察到预期方向。"
        }, "validator-b");
        claim = await service.TransitionAsync(projectId, claim.ClaimId, new MechanismClaimLifecycleRequest
        { TargetStatus = MechanismClaimStatuses.Active, Comment = "两轮真实运行均支持。" }, "approver");

        Assert.Equal(MechanismClaimStatuses.Active, claim.Status);
    }

    [Fact]
    public async Task Lifecycle_AllowsFormalFalsificationFromPromotedState()
    {
        var store = new MemoryMechanismKnowledgeStore();
        var service = CreateService(store);
        var projectId = Guid.CreateVersion7();
        var claim = await service.SaveDraftAsync(projectId, Draft(), "creator");
        claim = await service.ReviewAsync(
            claim.ClaimId, new MechanismClaimReviewRequest("approve", "结构正确"), "reviewer");

        claim = await service.TransitionAsync(projectId, claim.ClaimId, new MechanismClaimLifecycleRequest
        {
            TargetStatus = MechanismClaimStatuses.Falsified,
            EvidenceKind = "recipe-recommendation-outcome",
            ReferenceId = Guid.CreateVersion7().ToString(),
            ContentHash = new string('d', 64),
            ValidationHypothesisId = Guid.CreateVersion7(),
            EvaluationOutcome = "falsifies",
            EvaluationSummary = "真实运行结果明确未达到预注册最小效应。",
            Comment = "终止该声明。"
        }, "validator");

        Assert.Equal(MechanismClaimStatuses.Falsified, claim.Status);
    }

    [Fact]
    public async Task Draft_NormalizesControlledScopeAndEngineeringUnits()
    {
        var service = CreateService(new MemoryMechanismKnowledgeStore());
        var saved = await service.SaveDraftAsync(Guid.CreateVersion7(), Draft() with
        {
            Variables = [new MechanismClaimVariable { VariableCode = "temperature", VariableRole = "cause", Unit = "℃" }],
            Applicability = [new MechanismClaimApplicability { DimensionCode = "Material", DimensionValue = "Material-A" }],
            Constraints = [new MechanismClaimConstraint { VariableCode = "temperature", ConstraintKind = "range", Maximum = 530, Unit = "°C" }]
        }, "engineer");

        Assert.Equal("Cel", saved.Variables[0].Unit);
        Assert.Equal("Cel", saved.Constraints[0].Unit);
        Assert.Equal("material", saved.Applicability[0].DimensionCode);
        Assert.Equal("material-a", saved.Applicability[0].DimensionValue);
    }

    [Fact]
    public async Task Draft_NormalizesAndValidatesForbiddenCombination()
    {
        var service = CreateService(new MemoryMechanismKnowledgeStore());
        var saved = await service.SaveDraftAsync(Guid.CreateVersion7(), Draft() with
        {
            ForbiddenCombinations =
            [
                new MechanismForbiddenCombination
                {
                    Name = "高温和长保压联合禁区",
                    Factors =
                    [
                        new MechanismForbiddenCombinationFactor
                            { VariableCode = "holding.temperature", Minimum = 520, Unit = "°C" },
                        new MechanismForbiddenCombinationFactor
                            { VariableCode = "holding.time", Minimum = 12, Unit = "s" }
                    ]
                }
            ]
        }, "engineer");

        Assert.Single(saved.ForbiddenCombinations);
        Assert.NotEqual(Guid.Empty, saved.ForbiddenCombinations[0].CombinationId);
        Assert.Equal("Cel", saved.ForbiddenCombinations[0].Factors[0].Unit);
        Assert.Equal("s", saved.ForbiddenCombinations[0].Factors[1].Unit);
    }

    private static MechanismClaimVersion Draft() => new()
    {
        Name = "保压温度对缺陷的影响",
        MechanismType = "monotonic",
        Statement = "在指定材料与设备范围内，提高保压温度会降低未充满风险。",
        FalsificationCondition = "温度提高后未充满率没有下降，或材料降解指标恶化。",
        Variables = [new MechanismClaimVariable { VariableCode = "holding.temperature", VariableRole = "cause", Direction = "increase", Unit = "°C" }],
        Applicability = [new MechanismClaimApplicability { DimensionCode = "material", DimensionValue = "material-a" }],
        Evidence = [new MechanismClaimEvidence { EvidenceKind = "knowledge-fragment", ReferenceId = Guid.CreateVersion7().ToString(), ContentHash = new string('a', 64) }],
        ContentHash = "request-placeholder"
    };

    private static MechanismKnowledgeService CreateService(IMechanismKnowledgeStore store)
        => new(store, new TestResearchProjectContextReader());

    private sealed class TestResearchProjectContextReader : IResearchProjectContextReader
    {
        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<ResearchProject?>(new ResearchProject
            {
                ProjectId = projectId,
                Code = "mechanism-tests",
                Name = "机理知识测试项目",
                ProcessName = "test-process",
                MaterialName = "material-a",
                Variables =
                [
                    new ResearchVariable
                    {
                        Code = "holding.temperature",
                        Name = "保压温度",
                        Role = ResearchVariableRoles.Control,
                        Unit = "Cel"
                    },
                    new ResearchVariable
                    {
                        Code = "temperature",
                        Name = "温度",
                        Role = ResearchVariableRoles.Control,
                        Unit = "Cel"
                    },
                    new ResearchVariable
                    {
                        Code = "holding.time",
                        Name = "保压时间",
                        Role = ResearchVariableRoles.Control,
                        Unit = "s"
                    }
                ]
            });
    }

    private sealed class MissingResearchProjectContextReader : IResearchProjectContextReader
    {
        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<ResearchProject?>(null);
    }

    private sealed class MemoryMechanismKnowledgeStore : IMechanismKnowledgeStore
    {
        private readonly Dictionary<Guid, List<MechanismClaimVersion>> claims = [];
        private readonly List<MechanismClaimConflict> conflicts = [];
        private readonly HashSet<(Guid ClaimId, string ReferenceId)> lifecycleEvidence = [];
        private readonly HashSet<(Guid ClaimId, string UserId)> lifecycleActors = [];

        public Task<MechanismClaimVersion?> GetClaimAsync(Guid claimId, int? version = null, CancellationToken ct = default)
        {
            claims.TryGetValue(claimId, out var versions);
            return Task.FromResult(versions?.FirstOrDefault(value => value.Version == (version ?? versions.Max(item => item.Version))));
        }

        public Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MechanismClaimVersion>>(claims.Values.SelectMany(value => value).Where(value => value.ProjectId == projectId).ToArray());

        public Task<MechanismClaimVersion> SaveDraftAsync(MechanismClaimVersion value, CancellationToken ct = default)
        {
            if (!claims.TryGetValue(value.ClaimId, out var versions)) claims[value.ClaimId] = versions = [];
            versions.Add(value); return Task.FromResult(value);
        }

        public Task<bool> EvidenceExistsAsync(Guid projectId, MechanismClaimEvidence evidence, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<MechanismClaimVersion> AddReviewAsync(MechanismClaimReview review, string targetStatus, CancellationToken ct = default)
        {
            var versions = claims[review.ClaimId];
            var index = versions.FindIndex(value => value.Version == review.ClaimVersion);
            versions[index] = versions[index] with { Status = targetStatus, ReviewedBy = review.ReviewerId, ReviewedAt = review.ReviewedAt, UpdatedAt = review.ReviewedAt };
            return Task.FromResult(versions[index]);
        }

        public Task<MechanismClaimConflict> AddConflictAsync(MechanismClaimConflict value, CancellationToken ct = default)
        { conflicts.Add(value); return Task.FromResult(value); }

        public Task<MechanismClaimConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct = default)
            => Task.FromResult(conflicts.FirstOrDefault(value => value.ConflictId == conflictId));

        public Task<MechanismClaimConflict> ResolveConflictAsync(MechanismClaimConflict value, CancellationToken ct = default)
        {
            var index = conflicts.FindIndex(item => item.ConflictId == value.ConflictId);
            conflicts[index] = value;
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MechanismClaimConflict>>(conflicts.Where(value => value.ProjectId == projectId).ToArray());

        public Task SaveUsagesAsync(IReadOnlyList<MechanismClaimUsage> values, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveRecipeRecommendationUsagesAsync(
            IReadOnlyList<MechanismClaimUsage> values,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MechanismClaimUsage>>([]);

        public Task<bool> LifecycleEvidenceUsedAsync(Guid claimId, string referenceId, CancellationToken ct = default)
            => Task.FromResult(lifecycleEvidence.Contains((claimId, referenceId)));

        public Task<bool> LifecycleActorUsedAsync(Guid claimId, string userId, CancellationToken ct = default)
            => Task.FromResult(lifecycleActors.Contains((claimId, userId)));

        public Task<bool> RecipeRecommendationOutcomeSupportsClaimAsync(
            Guid projectId,
            MechanismClaimVersion claim,
            Guid validationHypothesisId,
            MechanismClaimEvidence evidence,
            string evaluationOutcome = "supports",
            CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<MechanismClaimVersion> TransitionAsync(MechanismClaimLifecycleDecision decision, CancellationToken ct = default)
        {
            var versions = claims[decision.ClaimId];
            var index = versions.FindIndex(value => value.Version == decision.ClaimVersion);
            versions[index] = versions[index] with { Status = decision.ToStatus, UpdatedAt = decision.DecidedAt };
            if (decision.ReferenceId is not null) lifecycleEvidence.Add((decision.ClaimId, decision.ReferenceId));
            lifecycleActors.Add((decision.ClaimId, decision.DecidedBy));
            return Task.FromResult(versions[index]);
        }
    }
}
