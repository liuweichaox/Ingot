using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

public sealed class ResearchAssetApplication(IResearchAssetStore assets)
{
    public Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default)
        => assets.ListDatasetsAsync(ct);
    public Task<ResearchAssetPage<TrainingDatasetVersion>> ListDatasetsPageAsync(
        int limit, string? cursor, CancellationToken ct = default)
        => assets.ListDatasetsPageAsync(limit, cursor, ct);
    public Task<TrainingDatasetVersion?> GetDatasetAsync(string id, int version, CancellationToken ct = default)
        => assets.GetDatasetAsync(id, version, ct);
    public Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default)
        => assets.ListModelsAsync(ct);
    public Task<ResearchAssetPage<ProcessModelVersion>> ListModelsPageAsync(
        int limit, string? cursor, CancellationToken ct = default)
        => assets.ListModelsPageAsync(limit, cursor, ct);
    public Task<ProcessModelVersion?> GetModelAsync(string id, int version, CancellationToken ct = default)
        => assets.GetModelAsync(id, version, ct);
    public Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(
        string id, int version, CancellationToken ct = default)
        => assets.ListEvaluationsAsync(id, version, ct);
    public Task<ResearchAssetPage<ModelEvaluation>> ListEvaluationsPageAsync(
        string id, int version, int limit, string? cursor, CancellationToken ct = default)
        => assets.ListEvaluationsPageAsync(id, version, limit, cursor, ct);
    public Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(
        string id, int version, CancellationToken ct = default)
        => assets.ListDriftReadingsAsync(id, version, ct);
    public Task<ResearchAssetPage<ModelDriftReading>> ListDriftReadingsPageAsync(
        string id, int version, int limit, string? cursor, CancellationToken ct = default)
        => assets.ListDriftReadingsPageAsync(id, version, limit, cursor, ct);
    public Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(CancellationToken ct = default)
        => assets.ListMechanismModelsAsync(ct);
    public Task<ResearchAssetPage<MechanismModelVersion>> ListMechanismModelsPageAsync(
        int limit, string? cursor, CancellationToken ct = default)
        => assets.ListMechanismModelsPageAsync(limit, cursor, ct);
    public Task<MechanismModelVersion?> GetMechanismModelAsync(
        string id, int version, CancellationToken ct = default)
        => assets.GetMechanismModelAsync(id, version, ct);
    public Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(CancellationToken ct = default)
        => assets.ListMechanismFusionsAsync(ct);
    public Task<ResearchAssetPage<MechanismFusionDefinition>> ListMechanismFusionsPageAsync(
        int limit, string? cursor, CancellationToken ct = default)
        => assets.ListMechanismFusionsPageAsync(limit, cursor, ct);
    public Task<MechanismFusionDefinition?> GetMechanismFusionAsync(
        string id, int version, CancellationToken ct = default)
        => assets.GetMechanismFusionAsync(id, version, ct);
    public Task<IReadOnlyList<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsAsync(
        CancellationToken ct = default)
        => assets.ListDatasetQualityValidationReportsAsync(ct);
    public Task<ResearchAssetPage<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsPageAsync(
        int limit, string? cursor, CancellationToken ct = default)
        => assets.ListDatasetQualityValidationReportsPageAsync(limit, cursor, ct);
    public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(
        string siteCode, CancellationToken ct = default)
        => assets.ListKnowledgeSourcesAsync(siteCode, ct);
    public Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesPageAsync(
        string siteCode, int limit, string? cursor, CancellationToken ct = default)
        => assets.ListKnowledgeSourcesPageAsync(siteCode, limit, cursor, ct);
    public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid id, CancellationToken ct = default)
        => assets.GetKnowledgeSourceAsync(id, ct);
    public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(Guid id, CancellationToken ct = default)
        => assets.ListKnowledgeRecordsAsync(id, ct);
    public Task<Stream?> OpenKnowledgeSourceAsync(Guid id, CancellationToken ct = default)
        => assets.OpenKnowledgeSourceAsync(id, ct);
    public Task<KnowledgeSource> AddKnowledgeSourceAsync(
        Stream content,
        string title,
        string sourceKind,
        string fileName,
        string mediaType,
        string siteCode,
        IReadOnlyDictionary<string, string> contextSelector,
        string userId,
        CancellationToken ct = default)
        => assets.AddKnowledgeSourceAsync(
            content, title, sourceKind, fileName, mediaType, siteCode, contextSelector, userId, ct);
    public Task EnqueueKnowledgeExtractionAsync(Guid id, string userId, CancellationToken ct = default)
        => assets.EnqueueKnowledgeExtractionAsync(id, userId, ct);
    public Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default)
        => assets.AddAuditEntryAsync(value, ct);
    public Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(
        string resourceType, string resourceId, CancellationToken ct = default)
        => assets.ListAuditEntriesAsync(resourceType, resourceId, ct);
}

public sealed class MechanismKnowledgeQueries(IMechanismKnowledgeStore knowledge)
{
    public Task<MechanismClaimVersion?> GetClaimAsync(
        Guid id, int? version = null, CancellationToken ct = default)
        => knowledge.GetClaimAsync(id, version, ct);
    public Task<MechanismClaimConflict?> GetConflictAsync(Guid id, CancellationToken ct = default)
        => knowledge.GetConflictAsync(id, ct);
    public Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(
        Guid recommendationId, CancellationToken ct = default)
        => knowledge.ListUsagesAsync(recommendationId, ct);
}
