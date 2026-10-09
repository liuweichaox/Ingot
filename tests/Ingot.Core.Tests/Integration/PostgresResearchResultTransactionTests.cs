// 验证配方建议证据链在 PostgreSQL 中按站点与配方隔离、只追加写入并拒绝覆盖。

using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresResearchResultTransactionTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task RecipeRecommendation_ShouldBeScopedBySiteAndRecipeAndDeduplicated()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var recommendation = Recommendation($"site-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);

        await store.CreateRecipeRecommendationAsync(recommendation);

        var persisted = await store.GetRecipeRecommendationAsync(recommendation.RecommendationId);
        Assert.Equal(recommendation.InputHash, persisted?.InputHash);
        Assert.Equal(recommendation.BriefHash, persisted?.BriefHash);
        Assert.Equal(recommendation.Brief.ProcessSpecificationId, persisted?.Brief.ProcessSpecificationId);
        Assert.Equal(recommendation.RecommendationId,
            (await store.GetRecipeRecommendationByInputHashAsync(
                recommendation.SiteCode, recommendation.ProcessSpecificationId, recommendation.InputHash))
            ?.RecommendationId);
        Assert.Null(await store.GetRecipeRecommendationByInputHashAsync(
            "other-site", recommendation.ProcessSpecificationId, recommendation.InputHash));
        Assert.Equal(recommendation.RecommendationId, Assert.Single(
            (await store.ListRecipeRecommendationsPageAsync(
                new RecipeRecommendationFilter([recommendation.SiteCode], recommendation.ProcessSpecificationId),
                null, 100)).Items).RecommendationId);
        Assert.Empty((await store.ListRecipeRecommendationsPageAsync(
            new RecipeRecommendationFilter(["other-site"]), null, 100)).Items);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateRecipeRecommendationAsync(
                recommendation with { RecommendationId = Guid.CreateVersion7() }));
    }

    [LinuxDockerFact]
    public async Task RecipeRecommendationDecision_ShouldBeAppendOnly()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var recommendation = Recommendation($"site-{Guid.NewGuid():N}", now);
        await store.CreateRecipeRecommendationAsync(recommendation);
        var decision = new ResearchRecipeRecommendationDecision
        {
            DecisionId = Guid.CreateVersion7(),
            RecommendationId = recommendation.RecommendationId,
            RecommendationKey = "suggestion-1",
            SiteCode = recommendation.SiteCode,
            ProcessSpecificationId = recommendation.ProcessSpecificationId,
            BriefHash = recommendation.BriefHash,
            Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
            Prediction = recommendation.Items[0].Prediction,
            DecisionSnapshotHash = new string('b', 64),
            DecidedBy = "engineer-b",
            DecidedAt = now.AddMinutes(1)
        };
        var executionKey = $"production-{Guid.NewGuid():N}";

        await store.CreateRecipeRecommendationDecisionTransactionAsync(decision, executionKey);
        Assert.Equal(decision.DecisionId, (await store.GetRecipeRecommendationDecisionByItemAsync(
            recommendation.RecommendationId, "suggestion-1"))!.DecisionId);
        Assert.Equal(decision.DecisionId, Assert.Single(
            await store.ListPendingRecipeRecommendationDecisionsAsync(
                recommendation.SiteCode, recommendation.ProcessSpecificationId)).DecisionId);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateRecipeRecommendationDecisionTransactionAsync(
                decision with { DecisionId = Guid.CreateVersion7() }, executionKey));

        var outcome = new ResearchRecipeRecommendationOutcome
        {
            BriefHash = recommendation.BriefHash,
            ActualExecutionKey = executionKey,
            SourceContentHash = new string('c', 64),
            CapturedAt = now.AddMinutes(2)
        };
        await store.AttachRecipeRecommendationOutcomeTransactionAsync(decision.DecisionId, outcome, "engineer-c");
        Assert.Equal(new string('c', 64), (await store.GetRecipeRecommendationDecisionAsync(
            decision.DecisionId))!.Outcome!.SourceContentHash);
        var repeated = await store.AttachRecipeRecommendationOutcomeTransactionAsync(
            decision.DecisionId, outcome with { SourceContentHash = new string('d', 64) }, "engineer-d");
        Assert.Equal(new string('c', 64), repeated.Outcome!.SourceContentHash);
        Assert.Empty(await store.ListPendingRecipeRecommendationDecisionsAsync(
            recommendation.SiteCode, recommendation.ProcessSpecificationId));
    }

    private static ResearchRecipeRecommendation Recommendation(string siteCode, DateTimeOffset now)
    {
        var brief = new RecipeRecommendationBrief
        {
            SiteCode = siteCode,
            ProcessSpecificationId = "recipe-store-spec",
            Name = "Recipe recommendation store"
        };
        return new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            SiteCode = siteCode,
            ProcessSpecificationId = brief.ProcessSpecificationId,
            Brief = brief,
            BriefHash = RecipeRecommendationBriefPolicy.Hash(brief),
            ModelVersion = "recipe-model-v1",
            InputHash = new string('a', 64),
            ObservationCount = 3,
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
            GeneratedAt = now,
            ExpiresAt = now.AddHours(24)
        };
    }
}
