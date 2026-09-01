// 提供流程测试使用的内存研发存储。
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class MemoryStore : IProcessResearchStore
    {
        private readonly Dictionary<Guid, ResearchProject> projects = [];
        private readonly Dictionary<Guid, ResearchValidationPreregistration> preregistrations = [];
        private readonly Dictionary<Guid, ResearchHypothesis> hypotheses = [];
        private readonly Dictionary<Guid, ResearchRecipeRecommendation> recommendations = [];
        private readonly Dictionary<Guid, ResearchRecipeRecommendationDecision> decisions = [];
        private readonly Dictionary<Guid, ResearchOperatingRegion> operatingRegions = [];
        private readonly Dictionary<Guid, ResearchKnowledgeClaim> knowledgeClaims = [];
        private readonly List<ResearchAuditEntry> auditEntries = [];

        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(projects.GetValueOrDefault(projectId));

        public Task<ResearchProject?> GetProjectByCodeAsync(string code, CancellationToken ct = default)
            => Task.FromResult(projects.Values.SingleOrDefault(value =>
                string.Equals(value.Code, code, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
            string userId, bool includeAll, IReadOnlyCollection<string>? siteIds, int limit,
            int offset, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchProject>>(projects.Values
                .Where(value => includeAll || value.MemberUserIds.Contains(userId))
                .Where(value => includeAll ||
                    (!string.IsNullOrWhiteSpace(value.SiteCode) && siteIds is not null &&
                     siteIds.Contains(value.SiteCode, StringComparer.OrdinalIgnoreCase)))
                .Skip(offset).Take(limit).ToArray());

        public Task<ResearchProject> SaveProjectAsync(ResearchProject value, CancellationToken ct = default)
        {
            projects[value.ProjectId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
            Guid preregistrationId, CancellationToken ct = default)
            => Task.FromResult(preregistrations.GetValueOrDefault(preregistrationId));

        public Task<IReadOnlyList<ResearchValidationPreregistration>> ListValidationPreregistrationsAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchValidationPreregistration>>(preregistrations.Values
                .Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchValidationPreregistration> CreateValidationPreregistrationAsync(
            ResearchValidationPreregistration value, CancellationToken ct = default)
        {
            preregistrations.Add(value.PreregistrationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchValidationPreregistration> ReviewValidationPreregistrationAsync(
            ResearchValidationPreregistration value, CancellationToken ct = default)
        {
            if (!preregistrations.TryGetValue(value.PreregistrationId, out var current) ||
                current.Status != ResearchValidationPreregistrationStatuses.Frozen)
                throw new ProcessResearchRuleException("阶段 0 预注册不存在或已经复核。");
            preregistrations[value.PreregistrationId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default)
            => Task.FromResult(hypotheses.GetValueOrDefault(hypothesisId));

        public Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchHypothesis>>(hypotheses.Values
                .Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchHypothesis> SaveHypothesisAsync(ResearchHypothesis value, CancellationToken ct = default)
        {
            hypotheses[value.HypothesisId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
            Guid recommendationId, CancellationToken ct = default)
            => Task.FromResult(recommendations.GetValueOrDefault(recommendationId));

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
            Guid projectId, string inputHash, CancellationToken ct = default)
            => Task.FromResult(recommendations.Values.FirstOrDefault(value =>
                value.ProjectId == projectId && string.Equals(value.InputHash, inputHash, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchRecipeRecommendation>> ListRecipeRecommendationsAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchRecipeRecommendation>>(recommendations.Values
                .Where(value => value.ProjectId == projectId)
                .OrderByDescending(static value => value.GeneratedAt).ToArray());

        public async Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
            Guid projectId, string? cursor, int limit, CancellationToken ct = default)
            => new() { Items = (await ListRecipeRecommendationsAsync(projectId, ct)).Take(limit).ToArray() };

        public async Task<ResearchRecipeRecommendation> CreateRecipeRecommendationTransactionAsync(
            ResearchRecipeRecommendation value, ResearchAuditEntry audit, CancellationToken ct = default)
        {
            if (recommendations.Values.Any(item => item.ProjectId == value.ProjectId && item.InputHash == value.InputHash))
                throw new ProcessResearchRuleException("相同输入快照的配方建议已经生成，请刷新后重试。");
            recommendations.Add(value.RecommendationId, value);
            await AddAuditEntryAsync(audit, ct);
            return value;
        }

        public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
            Guid decisionId, CancellationToken ct = default)
            => Task.FromResult(decisions.GetValueOrDefault(decisionId));

        public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
            Guid recommendationId, string recommendationKey, CancellationToken ct = default)
            => Task.FromResult(decisions.Values.SingleOrDefault(value =>
                value.RecommendationId == recommendationId && value.RecommendationKey == recommendationKey));

        public Task<ResearchPage<ResearchRecipeRecommendationDecision>> ListRecipeRecommendationDecisionsPageAsync(
            Guid projectId, string? cursor, int limit, CancellationToken ct = default)
            => Task.FromResult(new ResearchPage<ResearchRecipeRecommendationDecision>
            {
                Items = decisions.Values.Where(value => value.ProjectId == projectId)
                    .OrderByDescending(static value => value.DecidedAt).Take(limit).ToArray()
            });

        public async Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
            ResearchRecipeRecommendationDecision value, string? actualExecutionKey, ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            if (decisions.Values.Any(item => item.RecommendationId == value.RecommendationId &&
                item.RecommendationKey == value.RecommendationKey))
                throw new ProcessResearchRuleException("该配方建议项已经登记工程师决策。");
            if (!string.IsNullOrWhiteSpace(actualExecutionKey) && decisions.Values.Any(item =>
                item.ProjectId == value.ProjectId && item.ActualExecutionKey == actualExecutionKey))
                throw new ProcessResearchRuleException("该实际运行已经关联其他工程师决策。");
            var saved = value with { ActualExecutionKey = actualExecutionKey };
            decisions.Add(saved.DecisionId, saved);
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public async Task<ResearchRecipeRecommendationDecision> LinkRecipeRecommendationDecisionExecutionTransactionAsync(
            Guid decisionId, string actualExecutionKey, ResearchAuditEntry audit, CancellationToken ct = default)
        {
            if (!decisions.TryGetValue(decisionId, out var current))
                throw new ProcessResearchRuleException("日常建议决策不存在。");
            if (current.ActualExecutionKey == actualExecutionKey)
                return current;
            if (!string.IsNullOrWhiteSpace(current.ActualExecutionKey) || decisions.Values.Any(item =>
                item.ProjectId == current.ProjectId && item.ActualExecutionKey == actualExecutionKey))
                throw new ProcessResearchRuleException("该工程师决定或实际运行已经关联，不能覆盖。");
            var linked = current with { ActualExecutionKey = actualExecutionKey };
            decisions[decisionId] = linked;
            await AddAuditEntryAsync(audit, ct);
            return linked;
        }

        public async Task<ResearchRecipeRecommendationDecision> AttachRecipeRecommendationOutcomeTransactionAsync(
            Guid decisionId, ResearchRecipeRecommendationOutcome outcome, ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            if (!decisions.TryGetValue(decisionId, out var current))
                throw new ProcessResearchRuleException("日常建议决策不存在。");
            if (current.Outcome is not null)
                return current;
            var saved = current with { Outcome = outcome };
            decisions[decisionId] = saved;
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(Guid operatingRegionId, CancellationToken ct = default)
            => Task.FromResult(operatingRegions.GetValueOrDefault(operatingRegionId));

        public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchOperatingRegion>>(operatingRegions.Values
                .Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
            ResearchOperatingRegion value, CancellationToken ct = default)
        {
            operatingRegions[value.OperatingRegionId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(Guid claimId, CancellationToken ct = default)
            => Task.FromResult(knowledgeClaims.GetValueOrDefault(claimId));

        public Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchKnowledgeClaim>>(knowledgeClaims.Values
                .Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
            ResearchKnowledgeClaim value, CancellationToken ct = default)
        {
            knowledgeClaims[value.ClaimId] = value;
            return Task.FromResult(value);
        }

        public Task AddAuditEntryAsync(ResearchAuditEntry value, CancellationToken ct = default)
        {
            auditEntries.Add(value);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchAuditEntry>>(auditEntries
                .Where(value => value.ProjectId == projectId)
                .OrderBy(static value => value.CreatedAt).ToArray());

        public async Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
            Guid projectId, string? cursor, int limit, CancellationToken ct = default)
            => new() { Items = (await ListAuditEntriesAsync(projectId, ct)).Take(limit).ToArray() };
    }
}
