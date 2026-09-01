
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

// 研发工作流的知识声明写入边界；声明须由建议决策关联的真实运行结果支持。
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
                    "知识声明只能引用经过跨区组重复真实运行确认的工艺操作域。");
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
        var saved = await store.SaveKnowledgeClaimAsync(
            request with
            {
                ClaimId = existing?.ClaimId ??
                          (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId),
                ProjectId = projectId,
                TransferAssessmentId = null,
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
