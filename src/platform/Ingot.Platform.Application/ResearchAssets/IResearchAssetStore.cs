// 定义数据集、模型、机理知识和提取任务的研究资产存储端口。
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>保存可审计的研究资产及其显式提取和质量验证生命周期。</summary>
public interface IResearchAssetStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<TrainingDatasetVersion> AddDatasetAsync(TrainingDatasetVersion value, CancellationToken ct = default);
    Task<TrainingDatasetVersion?> GetDatasetAsync(string datasetId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default);
    Task<ResearchAssetPage<TrainingDatasetVersion>> ListDatasetsPageAsync(
        int limit, string? cursor, CancellationToken ct = default);

    Task<ProcessModelVersion> SaveModelAsync(ProcessModelVersion value, CancellationToken ct = default);
    Task<ProcessModelVersion?> GetModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default);
    Task<ResearchAssetPage<ProcessModelVersion>> ListModelsPageAsync(
        int limit, string? cursor, CancellationToken ct = default);
    Task<ModelEvaluation> AddEvaluationAsync(ModelEvaluation value, CancellationToken ct = default);
    Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(
        string modelId,
        int version,
        CancellationToken ct = default);
    Task<ResearchAssetPage<ModelEvaluation>> ListEvaluationsPageAsync(
        string modelId, int version, int limit, string? cursor, CancellationToken ct = default);
    Task<ModelDriftReading> AddDriftReadingAsync(ModelDriftReading value, CancellationToken ct = default);
    Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(
        string modelId,
        int version,
        CancellationToken ct = default);
    Task<ResearchAssetPage<ModelDriftReading>> ListDriftReadingsPageAsync(
        string modelId, int version, int limit, string? cursor, CancellationToken ct = default);

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
    Task<DatasetQualityValidationReport> SaveDatasetQualityValidationReportAsync(
        DatasetQualityValidationReport value,
        CancellationToken ct = default);
    Task<IReadOnlyList<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsAsync(
        CancellationToken ct = default);
    Task<ResearchAssetPage<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsPageAsync(
        int limit, string? cursor, CancellationToken ct = default);

    Task<KnowledgeSource> AddKnowledgeSourceAsync(
        Stream content,
        string title,
        string sourceKind,
        string fileName,
        string mediaType,
        IReadOnlyDictionary<string, string> contextSelector,
        string userId,
        CancellationToken ct = default);
    Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default);
    Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesPageAsync(
        Guid projectId, int limit, string? cursor, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(
        Guid projectId,
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

    Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(
        string resourceType,
        string resourceId,
        CancellationToken ct = default);
}
