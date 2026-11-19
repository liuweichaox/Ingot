using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>管理知识来源状态和知识记录的应用规则与审计。</summary>
public sealed class ResearchAssetWorkflow(
    IResearchAssetStore store,
    IKnowledgeEmbeddingQueue? embeddingQueue = null)
{
    public async Task<KnowledgeSource> ChangeKnowledgeSourceStatusAsync(
        Guid sourceId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ResearchAssetRuleException("知识来源目标状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        if (!KnowledgeSourceStatuses.IsValid(targetStatus))
            throw new ResearchAssetRuleException("知识来源目标状态无效。");
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        var allowed = source.Status switch
        {
            KnowledgeSourceStatuses.Uploaded => targetStatus is KnowledgeSourceStatuses.Indexed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Indexed => targetStatus is KnowledgeSourceStatuses.Reviewed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Reviewed => targetStatus == KnowledgeSourceStatuses.Retired,
            _ => false
        };
        if (source.Status != targetStatus && !allowed)
            throw new ResearchAssetRuleException($"不允许从 {source.Status} 转换到 {targetStatus}。");
        if (source.Status == targetStatus)
            return source;
        if (targetStatus == KnowledgeSourceStatuses.Reviewed)
        {
            var records = await store.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false);
            if (records.Count == 0 || records.Any(record => !record.HumanReviewed))
                throw new ResearchAssetRuleException("知识来源至少需要一条且全部知识记录完成人工复核。");
        }
        var now = DateTimeOffset.UtcNow;
        var updated = source with
        {
            Status = targetStatus,
            ReviewedBy = targetStatus == KnowledgeSourceStatuses.Reviewed ? userId : source.ReviewedBy,
            ReviewedAt = targetStatus == KnowledgeSourceStatuses.Reviewed ? now : source.ReviewedAt
        };
        await store.SaveKnowledgeSourceMetadataAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "knowledge-source",
            sourceId.ToString(),
            "status-changed",
            source.Status,
            targetStatus,
            userId,
            ct).ConfigureAwait(false);
        if (targetStatus == KnowledgeSourceStatuses.Reviewed && embeddingQueue is not null)
            await embeddingQueue.EnqueueAsync(sourceId, userId, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<KnowledgeRecord> SaveKnowledgeRecordAsync(
        KnowledgeRecord request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ResearchAssetValidator.TryValidate(
                request with
                {
                    CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? userId : request.CreatedBy,
                    ReviewedBy = request.HumanReviewed ? userId : null,
                    ReviewedAt = request.HumanReviewed ? DateTimeOffset.UtcNow : null
                },
                out var value,
                out var error))
            throw new ResearchAssetRuleException(error!);
        var source = await store.GetKnowledgeSourceAsync(value!.SourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        if (source.Status == KnowledgeSourceStatuses.Retired)
            throw new ResearchAssetRuleException("已停用的知识来源不能新增或修改知识记录。");
        var saved = await store.SaveKnowledgeRecordAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "knowledge-source",
            source.SourceId.ToString(),
            saved.HumanReviewed ? "record-reviewed" : "record-saved",
            source.Status,
            source.Status,
            userId,
            ct,
            new Dictionary<string, string> { ["recordId"] = saved.RecordId.ToString() }).ConfigureAwait(false);
        if (source.Status == KnowledgeSourceStatuses.Reviewed && saved.HumanReviewed && embeddingQueue is not null)
            await embeddingQueue.EnqueueAsync(source.SourceId, userId, ct).ConfigureAwait(false);
        return saved;
    }

    private Task AuditAsync(
        string resourceType,
        string resourceId,
        string action,
        string? fromStatus,
        string? toStatus,
        string userId,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? details = null)
        => store.AddAuditEntryAsync(
            new ResearchAssetAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = userId,
                Details = details ?? new Dictionary<string, string>(),
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct);
}

/// <summary>表示知识来源或知识记录违反应用规则。</summary>
public sealed class ResearchAssetRuleException(string message) : InvalidOperationException(message);
