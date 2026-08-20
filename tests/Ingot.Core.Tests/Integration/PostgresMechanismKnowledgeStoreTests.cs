// 验证 PostgresMechanismKnowledgeStore 的真实基础设施集成、失败和恢复行为。

using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresMechanismKnowledgeStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ListUsages_ShouldReturnAndVerifyTheExactAppliedClaimVersion()
    {
        await postgres.EnsureSchemaAsync();
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var recommendationId = Guid.CreateVersion7();
        var researchStore = new PostgresProcessResearchStore(postgres.DataSource);
        await researchStore.SaveProjectAsync(new ResearchProject
        {
            ProjectId = projectId,
            Code = $"mechanism-{projectId:N}",
            Name = "机理知识读取验证",
            ProcessName = "测试过程",
            OwnerUserId = "engineer-a",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await researchStore.SaveExperimentAsync(new ResearchExperiment
        {
            ExperimentId = recommendationId,
            ProjectId = projectId,
            Name = "冻结建议",
            StopRule = "触发边界时停止。",
            RollbackPlan = "恢复上一批准条件。",
            CreatedBy = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now
        });

        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Version = 1,
            Status = MechanismClaimStatuses.Active,
            Name = "冻结安全边界",
            MechanismType = "constraint",
            Statement = "控制变量必须保持在验证范围内。",
            FalsificationCondition = "独立实验确认范围外仍稳定。",
            EvidenceLevel = "validated-experiment",
            Variables =
            [
                new MechanismClaimVariable
                {
                    VariableCode = "temperature", VariableRole = "cause", Unit = "Cel"
                }
            ],
            Applicability =
            [
                new MechanismClaimApplicability
                    { DimensionCode = "project-code", DimensionValue = $"mechanism-{projectId:N}" }
            ],
            Constraints =
            [
                new MechanismClaimConstraint
                {
                    ConstraintId = Guid.CreateVersion7(), VariableCode = "temperature",
                    ConstraintKind = "safe-range", Minimum = 100, Maximum = 120,
                    Unit = "Cel", Severity = "hard"
                }
            ],
            ForbiddenCombinations =
            [
                new MechanismForbiddenCombination
                {
                    CombinationId = Guid.CreateVersion7(),
                    Name = "联合禁区",
                    Factors =
                    [
                        new MechanismForbiddenCombinationFactor
                            { VariableCode = "temperature", Minimum = 118, Unit = "Cel" },
                        new MechanismForbiddenCombinationFactor
                            { VariableCode = "pressure", Minimum = 8, Unit = "bar" }
                    ]
                }
            ],
            Evidence =
            [
                new MechanismClaimEvidence
                {
                    EvidenceLinkId = Guid.CreateVersion7(), EvidenceKind = "experiment-result",
                    ReferenceId = Guid.CreateVersion7().ToString(), Polarity = "supporting",
                    ContentHash = new string('b', 64)
                }
            ],
            CreatedBy = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = new string('a', 64)
        };
        var store = new PostgresMechanismKnowledgeStore(postgres.DataSource);
        await store.SaveDraftAsync(claim);
        await store.SaveUsagesAsync(
        [
            new MechanismClaimUsage
            {
                RecommendationId = recommendationId,
                ClaimId = claim.ClaimId,
                ClaimVersion = claim.Version,
                UsageType = "hard-constraint",
                ContentHash = claim.ContentHash
            }
        ]);
        await store.SaveDraftAsync(claim with
        {
            Version = 2,
            Name = "尚未用于该建议的新版本",
            Constraints = claim.Constraints.Select(value => value with
                { ConstraintId = Guid.CreateVersion7(), Maximum = 110 }).ToArray(),
            ForbiddenCombinations = claim.ForbiddenCombinations.Select(value => value with
                { CombinationId = Guid.CreateVersion7() }).ToArray(),
            Evidence = claim.Evidence.Select(value => value with
                { EvidenceLinkId = Guid.CreateVersion7() }).ToArray(),
            CreatedAt = now.AddMinutes(1),
            UpdatedAt = now.AddMinutes(1),
            ContentHash = new string('d', 64)
        });

        var usage = Assert.Single(await store.ListUsagesAsync(projectId));

        Assert.Equal("冻结安全边界", usage.ClaimName);
        Assert.NotNull(usage.AppliedClaim);
        Assert.Equal(1, usage.AppliedClaim.Version);
        Assert.Equal(claim.ContentHash, usage.AppliedClaim.ContentHash);
        Assert.Equal("temperature", Assert.Single(usage.AppliedClaim.Constraints).VariableCode);
        Assert.Equal("联合禁区", Assert.Single(usage.AppliedClaim.ForbiddenCombinations).Name);
        Assert.Equal("experiment-result", Assert.Single(usage.AppliedClaim.Evidence).EvidenceKind);

        await using var tamper = postgres.DataSource.CreateCommand(
            "UPDATE recommendation_knowledge_usage SET content_hash = @hash WHERE recommendation_id = @id;");
        tamper.Parameters.AddWithValue("hash", new string('c', 64));
        tamper.Parameters.AddWithValue("id", recommendationId);
        await tamper.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListUsagesAsync(projectId));
    }
}
