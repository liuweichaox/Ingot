// 以建议项为单位读取下一配方建议闭环状态；只读，不改变任何证据。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed class RecipeRecommendationQueries(IProcessResearchStore store)
{
    public Task<ResearchRecipeRecommendation?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
        => store.GetRecipeRecommendationAsync(recommendationId, ct);

    public Task<ResearchRecipeRecommendationDecision?> GetDecisionAsync(
        Guid decisionId,
        CancellationToken ct = default)
        => store.GetRecipeRecommendationDecisionAsync(decisionId, ct);

    public async Task<ResearchPage<ResearchRecipeRecommendationFlow>> ListFlowsAsync(
        RecipeRecommendationFilter filter,
        string? cursor,
        int limit,
        CancellationToken ct = default)
    {
        var page = await store.ListRecipeRecommendationsPageAsync(filter, cursor, limit, ct)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var flows = new List<ResearchRecipeRecommendationFlow>();
        foreach (var recommendation in page.Items)
        {
            foreach (var item in recommendation.Items)
            {
                var decision = await store.GetRecipeRecommendationDecisionByItemAsync(
                    recommendation.RecommendationId, item.RecommendationKey, ct).ConfigureAwait(false);
                var state = FlowState(recommendation, decision, now);
                flows.Add(new ResearchRecipeRecommendationFlow
                {
                    Recommendation = recommendation,
                    Item = item,
                    Decision = decision,
                    State = state,
                    AllowedActions = AllowedActions(state)
                });
            }
        }
        return new ResearchPage<ResearchRecipeRecommendationFlow>
        {
            Items = flows,
            NextCursor = page.NextCursor
        };
    }

    internal static string FlowState(
        ResearchRecipeRecommendation recommendation,
        ResearchRecipeRecommendationDecision? decision,
        DateTimeOffset now)
    {
        if (decision is null)
            return recommendation.ExpiresAt is null || recommendation.ExpiresAt > now
                ? ResearchRecipeRecommendationFlowStates.PendingDecision
                : ResearchRecipeRecommendationFlowStates.Stale;
        if (decision.Decision == ResearchRecipeRecommendationDecisionStatuses.Rejected)
            return ResearchRecipeRecommendationFlowStates.Rejected;
        if (string.IsNullOrWhiteSpace(decision.ActualExecutionKey))
            return ResearchRecipeRecommendationFlowStates.PendingExecution;
        if (decision.Outcome is null)
            return ResearchRecipeRecommendationFlowStates.PendingOutcome;
        return decision.Outcome.ValidForOptimization
            ? ResearchRecipeRecommendationFlowStates.OutcomeFrozen
            : ResearchRecipeRecommendationFlowStates.OutcomeExcluded;
    }

    private static IReadOnlyList<string> AllowedActions(string state)
        => state switch
        {
            ResearchRecipeRecommendationFlowStates.PendingDecision =>
                [ResearchRecipeRecommendationFlowActions.Decide],
            ResearchRecipeRecommendationFlowStates.PendingExecution =>
                [ResearchRecipeRecommendationFlowActions.LinkExecution],
            ResearchRecipeRecommendationFlowStates.PendingOutcome =>
                [ResearchRecipeRecommendationFlowActions.MaterializeOutcome],
            _ => []
        };
}
