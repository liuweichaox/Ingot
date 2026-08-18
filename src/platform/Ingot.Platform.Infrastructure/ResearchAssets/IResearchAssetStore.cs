using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public interface IResearchAssetStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<TrainingDatasetVersion> AddDatasetAsync(TrainingDatasetVersion value, CancellationToken ct = default);
    Task<TrainingDatasetVersion?> GetDatasetAsync(string datasetId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default);

    Task<ProcessModelVersion> SaveModelAsync(ProcessModelVersion value, CancellationToken ct = default);
    Task<ProcessModelVersion?> GetModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default);
    Task<ModelEvaluation> AddEvaluationAsync(ModelEvaluation value, CancellationToken ct = default);
    Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(
        string modelId,
        int version,
        CancellationToken ct = default);
    Task<ModelDriftReading> AddDriftReadingAsync(ModelDriftReading value, CancellationToken ct = default);
    Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(
        string modelId,
        int version,
        CancellationToken ct = default);

    Task<MechanismModelVersion> SaveMechanismModelAsync(
        MechanismModelVersion value,
        CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support mechanism models.");
    Task<MechanismModelVersion?> GetMechanismModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => Task.FromResult<MechanismModelVersion?>(null);
    Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MechanismModelVersion>>([]);
    Task<MechanismFusionDefinition> SaveMechanismFusionAsync(
        MechanismFusionDefinition value,
        CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support mechanism fusion definitions.");
    Task<MechanismFusionDefinition?> GetMechanismFusionAsync(
        string fusionId,
        int version,
        CancellationToken ct = default)
        => Task.FromResult<MechanismFusionDefinition?>(null);
    Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MechanismFusionDefinition>>([]);
    Task<DatasetQualityValidationReport> SaveDatasetQualityValidationReportAsync(
        DatasetQualityValidationReport value,
        CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support scientific validation reports.");
    Task<IReadOnlyList<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatasetQualityValidationReport>>([]);

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
    async Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(
        Guid projectId,
        CancellationToken ct = default)
        => (await ListKnowledgeSourcesAsync(ct).ConfigureAwait(false))
            .Where(value => value.ContextSelector.TryGetValue("research-project-id", out var id) &&
                string.Equals(id, projectId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default);
    Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default);
    Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default);
    Task<KnowledgeSource> ReplaceExtractedKnowledgeRecordsAsync(
        KnowledgeSource source,
        IReadOnlyList<KnowledgeRecord> records,
        CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support atomic extraction replacement.");
    Task EnqueueKnowledgeExtractionAsync(Guid sourceId, string userId, CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support extraction jobs.");
    Task<KnowledgeExtractionJob?> ClaimKnowledgeExtractionAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default)
        => Task.FromResult<KnowledgeExtractionJob?>(null);
    Task<bool> RenewKnowledgeExtractionLeaseAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> CompleteKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default)
        => Task.FromResult(false);
    Task<KnowledgeExtractionFailureDisposition?> FailKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken ct = default)
        => Task.FromResult<KnowledgeExtractionFailureDisposition?>(null);
    Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default);

    Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(
        string resourceType,
        string resourceId,
        CancellationToken ct = default);
}

public sealed class ProcessKnowledgeOptions
{
    public string RootPath { get; init; } = "data/process-knowledge";
    public string? ArchiveRootPath { get; init; }
    public long MaxFileBytes { get; init; } = 50 * 1024 * 1024;
}
