// 承载研发工作流的 KnowledgeClaims 分部，复用统一授权与并发规则。

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed partial class ProcessResearchWorkflow
{
    public async Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        Guid projectId,
        ResearchKnowledgeClaim request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        ResearchOperatingRegion? referencedOperatingRegion = null;
        if (request.OperatingRegionId is { } operatingRegionId)
        {
            referencedOperatingRegion = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false);
            if (referencedOperatingRegion is null || referencedOperatingRegion.ProjectId != projectId ||
                referencedOperatingRegion.Status != OperatingRegionStatuses.Validated ||
                referencedOperatingRegion.ValidationLevel is not (
                    OperatingRegionValidationLevels.Laboratory or
                    OperatingRegionValidationLevels.Production))
                throw new ProcessResearchRuleException(
                    "知识声明只能引用经过跨区组重复实验验证的工艺操作域。");
        }
        ResearchTransferAssessment? referencedTransfer = null;
        if (request.TransferAssessmentId is { } assessmentId)
        {
            referencedTransfer = await store.GetTransferAssessmentAsync(assessmentId, ct)
                .ConfigureAwait(false);
            if (referencedTransfer is null || referencedTransfer.ProjectId != projectId ||
                referencedTransfer.Status != ResearchTransferAssessmentStatuses.Reviewed ||
                referencedTransfer.Outcome != ResearchTransferOutcomes.Beneficial ||
                referencedTransfer.TargetProjectRevision != project.Revision)
                throw new ProcessResearchRuleException(
                    "知识声明只能引用目标项目当前版本中经过独立复核且有收益的迁移评估。");
            var sourceOperatingRegion = await store.GetOperatingRegionAsync(
                referencedTransfer.SourceOperatingRegionId, ct).ConfigureAwait(false);
            if (sourceOperatingRegion?.Status != OperatingRegionStatuses.Validated ||
                sourceOperatingRegion.ValidationLevel != OperatingRegionValidationLevels.Production ||
                sourceOperatingRegion.AnalysisHash != referencedTransfer.SourceOperatingRegionAnalysisHash)
                throw new ProcessResearchRuleException("迁移评估引用的源工艺操作域已经失效。");
            var repeated = (await store.ListTransferAssessmentsAsync(projectId, ct)
                    .ConfigureAwait(false))
                .Where(value => value.SourceOperatingRegionId == referencedTransfer.SourceOperatingRegionId &&
                                value.Status == ResearchTransferAssessmentStatuses.Reviewed &&
                                value.Outcome == ResearchTransferOutcomes.Beneficial &&
                                value.TargetProjectRevision == project.Revision)
                .Select(static value => value.TransferResultId)
                .Distinct()
                .Count();
            if (repeated < 2)
                throw new ProcessResearchRuleException(
                    "迁移知识至少需要两次不同实测结果相对从零对照取得经复核收益。");
        }
        var now = DateTimeOffset.UtcNow;
        var existing = request.ClaimId == Guid.Empty
            ? null
            : await store.GetKnowledgeClaimAsync(request.ClaimId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("知识声明不属于当前项目。");
        if (existing?.Status is ResearchKnowledgeStatuses.Published or ResearchKnowledgeStatuses.Retired)
            throw new ProcessResearchRuleException("已发布或已停用的知识声明保持不可变。");

        var evidence = NormalizeEvidence(projectId, request.Evidence).ToList();
        if (referencedOperatingRegion is not null)
        {
            evidence.Add(CreateEvidence(
                projectId,
                EvidenceKinds.OperatingRegion,
                referencedOperatingRegion.OperatingRegionId.ToString(),
                "知识声明引用的已验证工艺操作域。",
                referencedOperatingRegion.AnalysisHash,
                now));
        }
        if (referencedTransfer is not null)
        {
            evidence.Add(CreateEvidence(
                projectId,
                EvidenceKinds.TransferAssessment,
                referencedTransfer.AssessmentId.ToString(),
                "知识声明引用的重复收益迁移评估。",
                referencedTransfer.RecordHash,
                now));
        }
        var saved = await store.SaveKnowledgeClaimAsync(
            request with
            {
                ClaimId = existing?.ClaimId ??
                          (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId),
                ProjectId = projectId,
                TransferAssessmentId = referencedTransfer?.AssessmentId,
                Statement = RequiredText(request.Statement, "知识声明", 8000),
                Applicability = RequiredText(request.Applicability, "知识适用范围", 8000),
                Status = ResearchKnowledgeStatuses.Draft,
                Evidence = evidence
                    .GroupBy(static value => (value.Kind, value.ReferenceId))
                    .Select(static group => group.First())
                    .ToArray(),
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                ReviewedBy = null,
                ReviewedAt = null,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "knowledge-claim", saved.ClaimId.ToString(),
            existing is null ? "created" : "updated",
            userId, existing?.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchKnowledgeClaim> ReviewKnowledgeClaimAsync(
        Guid claimId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetKnowledgeClaimAsync(claimId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("知识声明不存在。");
        await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        var actor = NormalizeUser(userId);
        if (value.Status != ResearchKnowledgeStatuses.Draft)
            throw new ProcessResearchRuleException("只有草稿知识声明可以审核。");
        if (string.Equals(value.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("知识声明创建人和审核人必须分离。");
        if (value.Evidence.Count == 0)
            throw new ProcessResearchRuleException("知识声明审核前必须关联证据。");

        var saved = await store.SaveKnowledgeClaimAsync(
            value with
            {
                Status = ResearchKnowledgeStatuses.Reviewed,
                ReviewedBy = actor,
                ReviewedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "knowledge-claim", claimId.ToString(), "reviewed",
            userId, value.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }
}
