// 验证 PostgresResearchResultTransaction 的真实基础设施集成、失败和恢复行为。

using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresResearchResultTransactionTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task RecipeRecommendation_ShouldUseIndependentAppendOnlyStorage()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"recipe-store-{Guid.NewGuid():N}",
            Name = "Recipe recommendation store",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            ModelVersion = "recipe-model-v1",
            InputHash = new string('a', 64),
            ObservationCount = 3,
            FeatureSetId = "recipe-features",
            MechanismKnowledgeSnapshotHash = "none",
            MechanismModelSnapshotHash = "none",
            CreatedBy = "engineer-a",
            GeneratedAt = now
        };
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation",
            ResourceId = recommendation.RecommendationId.ToString(),
            Action = "created",
            UserId = "engineer-a",
            CreatedAt = now
        };
        await store.SaveProjectAsync(project);

        await store.CreateRecipeRecommendationTransactionAsync(recommendation, audit);

        var persisted = await store.GetRecipeRecommendationAsync(recommendation.RecommendationId);
        Assert.Equal(recommendation.RecommendationId, persisted?.RecommendationId);
        Assert.Equal(recommendation.InputHash, persisted?.InputHash);
        Assert.Equal(recommendation.RecommendationId,
            (await store.GetRecipeRecommendationByInputHashAsync(
                project.ProjectId, recommendation.InputHash))?.RecommendationId);
        Assert.Equal(recommendation.RecommendationId, Assert.Single(
            (await store.ListRecipeRecommendationsPageAsync(project.ProjectId, null, 100)).Items)
            .RecommendationId);
        Assert.Equal("recipe-recommendation", Assert.Single(
            await store.ListAuditEntriesAsync(project.ProjectId)).ResourceType);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateRecipeRecommendationTransactionAsync(
                recommendation with { RecommendationId = Guid.CreateVersion7() },
                audit with { EntryId = Guid.CreateVersion7() }));
    }

    [LinuxDockerFact]
    public async Task RecipeRecommendationDecision_ShouldBeAppendOnlyAndAuditedTransactionally()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"recipe-decision-store-{Guid.NewGuid():N}",
            Name = "Recipe decision store",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            ModelVersion = "recipe-model-v1",
            InputHash = new string('a', 64),
            FeatureSetId = "recipe-features",
            MechanismKnowledgeSnapshotHash = "none",
            MechanismModelSnapshotHash = "none",
            Items =
            [
                new ResearchRecipeRecommendationItem
                {
                    RecommendationKey = "suggestion-1",
                    Prediction = new OptimizationRunPrediction
                    {
                        ExecutionKey = "suggestion-1",
                        Rationale = "test"
                    }
                }
            ],
            CreatedBy = "engineer-a",
            GeneratedAt = now
        };
        await store.SaveProjectAsync(project);
        await store.CreateRecipeRecommendationTransactionAsync(recommendation, new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation",
            ResourceId = recommendation.RecommendationId.ToString(),
            Action = "created",
            UserId = "engineer-a",
            CreatedAt = now
        });
        var decision = new ResearchRecipeRecommendationDecision
        {
            DecisionId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            RecommendationId = recommendation.RecommendationId,
            RecommendationKey = "suggestion-1",
            Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
            ActualExecutionKey = "production-1",
            Prediction = recommendation.Items[0].Prediction,
            DecisionSnapshotHash = new string('b', 64),
            DecidedBy = "engineer-b",
            DecidedAt = now.AddMinutes(1)
        };
        var decisionAudit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation-decision",
            ResourceId = decision.DecisionId.ToString(),
            Action = "decision-frozen",
            ToStatus = decision.Decision,
            UserId = decision.DecidedBy,
            CreatedAt = decision.DecidedAt
        };

        await store.CreateRecipeRecommendationDecisionTransactionAsync(
            decision, decision.ActualExecutionKey, decisionAudit);
        Assert.Equal(decision.DecisionId, (await store.GetRecipeRecommendationDecisionByItemAsync(
            recommendation.RecommendationId, "suggestion-1"))!.DecisionId);
        Assert.Equal(decision.DecisionId, Assert.Single(
            (await store.ListRecipeRecommendationDecisionsPageAsync(project.ProjectId, null, 100)).Items)
            .DecisionId);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateRecipeRecommendationDecisionTransactionAsync(
                decision with { DecisionId = Guid.CreateVersion7() },
                decision.ActualExecutionKey,
                decisionAudit with { EntryId = Guid.CreateVersion7() }));

        var outcome = new ResearchRecipeRecommendationOutcome
        {
            ActualExecutionKey = decision.ActualExecutionKey,
            SourceContentHash = new string('c', 64),
            CapturedAt = now.AddMinutes(2)
        };
        await store.AttachRecipeRecommendationOutcomeTransactionAsync(decision.DecisionId, outcome, decisionAudit with
        {
            EntryId = Guid.CreateVersion7(),
            Action = "source-outcome-frozen",
            CreatedAt = outcome.CapturedAt
        });
        Assert.Equal(new string('c', 64), (await store.GetRecipeRecommendationDecisionAsync(
            decision.DecisionId))!.Outcome!.SourceContentHash);
        var repeated = await store.AttachRecipeRecommendationOutcomeTransactionAsync(
            decision.DecisionId, outcome with { SourceContentHash = new string('d', 64) }, decisionAudit with
            {
                EntryId = Guid.CreateVersion7(),
                Action = "source-outcome-frozen"
            });
        Assert.Equal(new string('c', 64), repeated.Outcome!.SourceContentHash);
        Assert.Equal(3, (await store.ListAuditEntriesAsync(project.ProjectId)).Count);
    }
}
