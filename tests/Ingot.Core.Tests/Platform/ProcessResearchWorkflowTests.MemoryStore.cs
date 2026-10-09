// 提供配方建议闭环测试使用的内存存储，规则与 PostgreSQL 实现的唯一约束保持一致。
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class MemoryStore : IProcessResearchStore
    {
        private readonly Dictionary<Guid, ResearchRecipeRecommendation> recommendations = [];
        private readonly Dictionary<Guid, ResearchRecipeRecommendationDecision> decisions = [];

        public IReadOnlyList<ResearchRecipeRecommendation> Recommendations => recommendations.Values.ToArray();

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
            Guid recommendationId, CancellationToken ct = default)
            => Task.FromResult(recommendations.GetValueOrDefault(recommendationId));

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
            string siteCode, string processSpecificationId, string inputHash, CancellationToken ct = default)
            => Task.FromResult(recommendations.Values.FirstOrDefault(value =>
                value.SiteCode == siteCode &&
                value.ProcessSpecificationId == processSpecificationId &&
                value.InputHash == inputHash));

        public Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
            RecipeRecommendationFilter filter, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult(new ResearchPage<ResearchRecipeRecommendation>
            {
                Items = recommendations.Values
                    .Where(value => filter.SiteCodes is null ||
                        filter.SiteCodes.Contains(value.SiteCode, StringComparer.OrdinalIgnoreCase))
                    .Where(value => filter.ProcessSpecificationId is null ||
                        value.ProcessSpecificationId == filter.ProcessSpecificationId)
                    .OrderByDescending(static value => value.GeneratedAt)
                    .ThenByDescending(static value => value.RecommendationId)
                    .Take(limit)
                    .ToArray()
            });

        public Task<ResearchRecipeRecommendation> CreateRecipeRecommendationAsync(
            ResearchRecipeRecommendation value, CancellationToken ct = default)
        {
            if (recommendations.Values.Any(item =>
                    item.SiteCode == value.SiteCode &&
                    item.ProcessSpecificationId == value.ProcessSpecificationId &&
                    item.InputHash == value.InputHash))
                throw new ProcessResearchRuleException("相同输入快照的配方建议已经生成，请刷新后重试。");
            recommendations.Add(value.RecommendationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
            Guid decisionId, CancellationToken ct = default)
            => Task.FromResult(decisions.GetValueOrDefault(decisionId));

        public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
            Guid recommendationId, string recommendationKey, CancellationToken ct = default)
            => Task.FromResult(decisions.Values.SingleOrDefault(value =>
                value.RecommendationId == recommendationId && value.RecommendationKey == recommendationKey));

        public Task<IReadOnlyList<ResearchRecipeRecommendationDecision>> ListPendingRecipeRecommendationDecisionsAsync(
            string siteCode, string processSpecificationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchRecipeRecommendationDecision>>(decisions.Values
                .Where(value => value.SiteCode == siteCode &&
                    value.ProcessSpecificationId == processSpecificationId &&
                    value.Decision is ResearchRecipeRecommendationDecisionStatuses.Accepted or
                        ResearchRecipeRecommendationDecisionStatuses.Modified &&
                    value.Outcome is null)
                .OrderBy(static value => value.DecidedAt).ThenBy(static value => value.DecisionId).ToArray());

        public Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
            ResearchRecipeRecommendationDecision value, string? actualExecutionKey, CancellationToken ct = default)
        {
            if (decisions.Values.Any(item => item.RecommendationId == value.RecommendationId &&
                item.RecommendationKey == value.RecommendationKey))
                throw new ProcessResearchRuleException("该配方建议项已经登记工程师决策。");
            if (!string.IsNullOrWhiteSpace(actualExecutionKey) &&
                decisions.Values.Any(item => item.ActualExecutionKey == actualExecutionKey))
                throw new ProcessResearchRuleException("该实际运行已经关联其他工程师决策。");
            var saved = value with { ActualExecutionKey = actualExecutionKey };
            decisions.Add(saved.DecisionId, saved);
            return Task.FromResult(saved);
        }

        public Task<ResearchRecipeRecommendationDecision> LinkRecipeRecommendationDecisionExecutionTransactionAsync(
            Guid decisionId, string actualExecutionKey, string linkedBy, CancellationToken ct = default)
        {
            if (!decisions.TryGetValue(decisionId, out var current))
                throw new ProcessResearchRuleException("配方建议决定不存在。");
            if (current.ActualExecutionKey == actualExecutionKey)
                return Task.FromResult(current);
            if (!string.IsNullOrWhiteSpace(current.ActualExecutionKey) ||
                decisions.Values.Any(item => item.ActualExecutionKey == actualExecutionKey))
                throw new ProcessResearchRuleException("该工程师决定或实际运行已经关联，不能覆盖。");
            var linked = current with { ActualExecutionKey = actualExecutionKey };
            decisions[decisionId] = linked;
            return Task.FromResult(linked);
        }

        public Task<ResearchRecipeRecommendationDecision> AttachRecipeRecommendationOutcomeTransactionAsync(
            Guid decisionId, ResearchRecipeRecommendationOutcome outcome, string materializedBy,
            CancellationToken ct = default)
        {
            if (!decisions.TryGetValue(decisionId, out var current))
                throw new ProcessResearchRuleException("配方建议决定不存在。");
            if (current.Outcome is not null)
                return Task.FromResult(current);
            var saved = current with { Outcome = outcome };
            decisions[decisionId] = saved;
            return Task.FromResult(saved);
        }
    }
}
