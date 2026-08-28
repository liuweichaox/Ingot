// 提供流程测试使用的内存研究存储。
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class MemoryStore : IProcessResearchStore
    {
        private readonly Dictionary<Guid, ResearchProject> _projects = [];
        private readonly Dictionary<Guid, ResearchValidationPreregistration> _preregistrations = [];
        private readonly Dictionary<Guid, ResearchHypothesis> _hypotheses = [];
        private readonly Dictionary<Guid, ResearchRecipeRecommendation> _recipeRecommendations = [];
        private readonly Dictionary<Guid, ResearchExperiment> _experiments = [];
        private readonly Dictionary<Guid, ResearchShadowRecommendation> _shadowRecommendations = [];
        private readonly Dictionary<Guid, ResearchHistoricalReplayReport> _replayReports = [];
        private readonly Dictionary<Guid, ResearchRollbackDrill> _rollbackDrills = [];
        private readonly Dictionary<Guid, ResearchExperimentResult> _results = [];
        private readonly Dictionary<Guid, ResearchOperatingRegion> _windows = [];
        private readonly Dictionary<Guid, ResearchKnowledgeClaim> _claims = [];
        private readonly Dictionary<Guid, ResearchTransferAssessment> _transferAssessments = [];
        private readonly List<ResearchAuditEntry> _audit = [];

        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(_projects.GetValueOrDefault(projectId));

        public Task<ResearchProject?> GetProjectByCodeAsync(
            string code,
            CancellationToken ct = default)
            => Task.FromResult(_projects.Values.SingleOrDefault(
                value => string.Equals(value.Code, code, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
            string userId,
            bool includeAll,
            IReadOnlyCollection<string>? siteIds,
            int limit,
            int offset,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchProject>>(
                _projects.Values
                    .Where(value => includeAll || value.MemberUserIds.Contains(userId))
                    .Where(value => includeAll ||
                                    (!string.IsNullOrWhiteSpace(value.SiteCode) &&
                                     siteIds is not null &&
                                     siteIds.Contains(value.SiteCode, StringComparer.OrdinalIgnoreCase)))
                    .Skip(offset)
                    .Take(limit)
                    .ToArray());

        public Task<ResearchProject> SaveProjectAsync(
            ResearchProject value,
            CancellationToken ct = default)
        {
            _projects[value.ProjectId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
            Guid preregistrationId,
            CancellationToken ct = default)
            => Task.FromResult(_preregistrations.GetValueOrDefault(preregistrationId));

        public Task<IReadOnlyList<ResearchValidationPreregistration>> ListValidationPreregistrationsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchValidationPreregistration>>(
                _preregistrations.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchValidationPreregistration> CreateValidationPreregistrationAsync(
            ResearchValidationPreregistration value,
            CancellationToken ct = default)
        {
            _preregistrations.Add(value.PreregistrationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchValidationPreregistration> ReviewValidationPreregistrationAsync(
            ResearchValidationPreregistration value,
            CancellationToken ct = default)
        {
            if (!_preregistrations.TryGetValue(value.PreregistrationId, out var current) ||
                current.Status != ResearchValidationPreregistrationStatuses.Frozen)
                throw new ProcessResearchRuleException("阶段 0 预注册不存在或已经复核。");
            _preregistrations[value.PreregistrationId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchHypothesis?> GetHypothesisAsync(
            Guid hypothesisId,
            CancellationToken ct = default)
            => Task.FromResult(_hypotheses.GetValueOrDefault(hypothesisId));

        public Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchHypothesis>>(
                _hypotheses.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchHypothesis> SaveHypothesisAsync(
            ResearchHypothesis value,
            CancellationToken ct = default)
        {
            _hypotheses[value.HypothesisId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
            Guid recommendationId,
            CancellationToken ct = default)
            => Task.FromResult(_recipeRecommendations.GetValueOrDefault(recommendationId));

        public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
            Guid projectId,
            string inputHash,
            CancellationToken ct = default)
            => Task.FromResult(_recipeRecommendations.Values.FirstOrDefault(value =>
                value.ProjectId == projectId &&
                string.Equals(value.InputHash, inputHash, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchRecipeRecommendation>> ListRecipeRecommendationsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchRecipeRecommendation>>(
                _recipeRecommendations.Values
                    .Where(value => value.ProjectId == projectId)
                    .OrderByDescending(static value => value.GeneratedAt)
                    .ToArray());

        public async Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new()
            {
                Items = (await ListRecipeRecommendationsAsync(projectId, ct))
                    .Take(limit)
                    .ToArray()
            };

        public async Task<ResearchRecipeRecommendation> CreateRecipeRecommendationTransactionAsync(
            ResearchRecipeRecommendation value,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            if (_recipeRecommendations.Values.Any(item =>
                    item.ProjectId == value.ProjectId && item.InputHash == value.InputHash))
                throw new ProcessResearchRuleException("相同输入快照的配方建议已经生成，请刷新后重试。");
            _recipeRecommendations.Add(value.RecommendationId, value);
            await AddAuditEntryAsync(audit, ct);
            return value;
        }

        public Task<ResearchExperiment?> GetExperimentAsync(
            Guid experimentId,
            CancellationToken ct = default)
            => Task.FromResult(_experiments.GetValueOrDefault(experimentId));

        public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperiment>>(
                _experiments.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchExperiment>> ListExperimentsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListExperimentsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchExperiment> SaveExperimentAsync(
            ResearchExperiment value,
            CancellationToken ct = default)
        {
            _experiments[value.ExperimentId] = value;
            return Task.FromResult(value);
        }

        public async Task<ResearchExperiment> SaveExperimentTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            var saved = await SaveExperimentAsync(updatedExperiment, ct);
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
            => SaveExperimentTransactionAsync(updatedExperiment, audit, ct);

        public Task<ResearchShadowRecommendation?> GetShadowRecommendationAsync(
            Guid recommendationId,
            CancellationToken ct = default)
            => Task.FromResult(_shadowRecommendations.GetValueOrDefault(recommendationId));

        public Task<ResearchShadowRecommendation?> GetShadowRecommendationBySuggestionAsync(
            Guid experimentId,
            string suggestionExecutionKey,
            CancellationToken ct = default)
            => Task.FromResult(_shadowRecommendations.Values.SingleOrDefault(value =>
                value.ExperimentId == experimentId &&
                value.SuggestionExecutionKey == suggestionExecutionKey));

        public Task<IReadOnlyList<ResearchShadowRecommendation>> ListShadowRecommendationsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchShadowRecommendation>>(
                _shadowRecommendations.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchShadowRecommendation>> ListShadowRecommendationsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListShadowRecommendationsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchShadowRecommendation> CreateShadowRecommendationAsync(
            ResearchShadowRecommendation value,
            CancellationToken ct = default)
        {
            _shadowRecommendations.Add(value.RecommendationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchShadowRecommendation> AttachShadowOutcomeAsync(
            ResearchShadowRecommendation value,
            CancellationToken ct = default)
        {
            if (!_shadowRecommendations.TryGetValue(value.RecommendationId, out var current) ||
                current.Outcome is not null)
                throw new ProcessResearchRuleException("影子建议不存在，或结果已经冻结。");
            _shadowRecommendations[value.RecommendationId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchHistoricalReplayReport?> GetHistoricalReplayReportAsync(
            Guid reportId,
            CancellationToken ct = default)
            => Task.FromResult(_replayReports.GetValueOrDefault(reportId));

        public Task<IReadOnlyList<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchHistoricalReplayReport>>(
                _replayReports.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListHistoricalReplayReportsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchHistoricalReplayReport> CreateHistoricalReplayReportAsync(
            ResearchHistoricalReplayReport value,
            CancellationToken ct = default)
        {
            var existing = _replayReports.Values.FirstOrDefault(item =>
                item.ProjectId == value.ProjectId &&
                item.DatasetSnapshotHash == value.DatasetSnapshotHash &&
                item.ReportHash == value.ReportHash);
            if (existing is not null)
                return Task.FromResult(existing);
            _replayReports.Add(value.ReportId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchHistoricalReplayReport> ReviewHistoricalReplayReportAsync(
            ResearchHistoricalReplayReport value,
            CancellationToken ct = default)
        {
            if (!_replayReports.TryGetValue(value.ReportId, out var current) ||
                current.Status != ResearchHistoricalReplayStatuses.Generated)
                throw new ProcessResearchRuleException("历史回放报告不存在或已经审核。");
            _replayReports[value.ReportId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchRollbackDrill?> GetRollbackDrillAsync(
            Guid drillId,
            CancellationToken ct = default)
            => Task.FromResult(_rollbackDrills.GetValueOrDefault(drillId));

        public Task<IReadOnlyList<ResearchRollbackDrill>> ListRollbackDrillsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchRollbackDrill>>(
                _rollbackDrills.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchRollbackDrill> CreateRollbackDrillAsync(
            ResearchRollbackDrill value,
            CancellationToken ct = default)
        {
            _rollbackDrills.Add(value.DrillId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchRollbackDrill> ReviewRollbackDrillAsync(
            ResearchRollbackDrill value,
            CancellationToken ct = default)
        {
            if (!_rollbackDrills.TryGetValue(value.DrillId, out var current) ||
                current.Status != ResearchRollbackDrillStatuses.Recorded)
                throw new ProcessResearchRuleException("回退演练不存在或已经复核。");
            _rollbackDrills[value.DrillId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchExperimentResult?> GetExperimentResultAsync(
            Guid resultId,
            CancellationToken ct = default)
            => Task.FromResult(_results.GetValueOrDefault(resultId));

        public Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperimentResult>>(
                _results.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchExperimentResult>> ListExperimentResultsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListExperimentResultsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchExperimentResult> SaveExperimentResultAsync(
            ResearchExperimentResult value,
            CancellationToken ct = default)
        {
            _results[value.ResultId] = value;
            return Task.FromResult(value);
        }

        public async Task<ResearchExperimentResult> SaveExperimentResultTransactionAsync(
            ResearchExperimentResult result,
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            var saved = await SaveExperimentResultAsync(result, ct);
            await SaveExperimentAsync(updatedExperiment, ct);
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
            Guid operatingRegionId,
            CancellationToken ct = default)
            => Task.FromResult(_windows.GetValueOrDefault(operatingRegionId));

        public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchOperatingRegion>>(
                _windows.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
            ResearchOperatingRegion value,
            CancellationToken ct = default)
        {
            _windows[value.OperatingRegionId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(
            Guid claimId,
            CancellationToken ct = default)
            => Task.FromResult(_claims.GetValueOrDefault(claimId));

        public Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchKnowledgeClaim>>(
                _claims.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
            ResearchKnowledgeClaim value,
            CancellationToken ct = default)
        {
            _claims[value.ClaimId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchTransferAssessment?> GetTransferAssessmentAsync(
            Guid assessmentId,
            CancellationToken ct = default)
            => Task.FromResult(_transferAssessments.GetValueOrDefault(assessmentId));

        public Task<IReadOnlyList<ResearchTransferAssessment>> ListTransferAssessmentsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchTransferAssessment>>(
                _transferAssessments.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchTransferAssessment> CreateTransferAssessmentAsync(
            ResearchTransferAssessment value,
            CancellationToken ct = default)
        {
            var existing = _transferAssessments.Values.FirstOrDefault(item =>
                item.ProjectId == value.ProjectId && item.SourceOperatingRegionId == value.SourceOperatingRegionId &&
                item.RecordHash == value.RecordHash);
            if (existing is not null)
                return Task.FromResult(existing);
            _transferAssessments[value.AssessmentId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchTransferAssessment> ReviewTransferAssessmentAsync(
            ResearchTransferAssessment value,
            CancellationToken ct = default)
        {
            if (!_transferAssessments.TryGetValue(value.AssessmentId, out var current) ||
                current.Status != ResearchTransferAssessmentStatuses.Recorded)
                throw new ProcessResearchRuleException("迁移评估不存在或已经复核。");
            _transferAssessments[value.AssessmentId] = value;
            return Task.FromResult(value);
        }

        public Task AddAuditEntryAsync(
            ResearchAuditEntry value,
            CancellationToken ct = default)
        {
            _audit.Add(value);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchAuditEntry>>(
                _audit.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListAuditEntriesAsync(projectId, ct)).Take(limit).ToArray() };
    }

}
