// 定义机理知识、知识来源、提取任务和审计记录的存储端口。
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>保存可审计的机理知识、知识来源、提取任务和审计记录。</summary>
public interface IResearchAssetStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<MechanismModelVersion> SaveMechanismModelAsync(
        MechanismModelVersion value,
        CancellationToken ct = default);
    Task<MechanismModelVersion?> GetMechanismModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default);
    Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(
        CancellationToken ct = default);
    Task<ResearchAssetPage<MechanismModelVersion>> ListMechanismModelsPageAsync(
        int limit, string? cursor, CancellationToken ct = default);
    Task<MechanismFusionDefinition> SaveMechanismFusionAsync(
        MechanismFusionDefinition value,
        CancellationToken ct = default);
    Task<MechanismFusionDefinition?> GetMechanismFusionAsync(
        string fusionId,
        int version,
        CancellationToken ct = default);
    Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(
        CancellationToken ct = default);
    Task<ResearchAssetPage<MechanismFusionDefinition>> ListMechanismFusionsPageAsync(
        int limit, string? cursor, CancellationToken ct = default);
    Task<KnowledgeSource> AddKnowledgeSourceAsync(
        Stream content,
        string title,
        string sourceKind,
        string fileName,
        string mediaType,
        string siteCode,
        IReadOnlyDictionary<string, string> contextSelector,
        string userId,
        CancellationToken ct = default);
    Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default);
    Task<KnowledgeSource?> FindKnowledgeSourceByHashAsync(
        string siteCode,
        string sha256,
        CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default);
    Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesPageAsync(
        string siteCode, int limit, string? cursor, CancellationToken ct = default);
    Task<ResearchAssetPage<KnowledgeSource>> ListReviewedKnowledgeSourcesPageAsync(
        string siteCode,
        string? processSpecificationId,
        int limit,
        string? cursor,
        CancellationToken ct = default);
    Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesForManagementPageAsync(
        string siteCode,
        int limit,
        string? cursor,
        CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(
        string siteCode,
        CancellationToken ct = default);
    Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default);
    Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default);
    Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default);
    Task<KnowledgeSource> ReplaceExtractedKnowledgeRecordsAsync(
        KnowledgeSource source,
        IReadOnlyList<KnowledgeRecord> records,
        CancellationToken ct = default);
    Task EnqueueKnowledgeExtractionAsync(Guid sourceId, string userId, CancellationToken ct = default);
    Task<KnowledgeExtractionJob?> ClaimKnowledgeExtractionAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default);
    Task<bool> RenewKnowledgeExtractionLeaseAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default);
    Task<bool> CompleteKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default);
    Task<KnowledgeExtractionFailureDisposition?> FailKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default);
    Task<ResearchAssetPage<KnowledgeRecord>> ListKnowledgeRecordsForEmbeddingPageAsync(
        Guid sourceId,
        int limit,
        string? cursor,
        CancellationToken ct = default);

    Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(
        string resourceType,
        string resourceId,
        CancellationToken ct = default);
}
