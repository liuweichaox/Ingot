using Ingot.Contracts.ProcessImprovement;

namespace Ingot.Platform.Infrastructure.ProcessImprovement;

public interface IProcessImprovementStore
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
    Task<ScientificValidationReport> SaveScientificValidationReportAsync(
        ScientificValidationReport value,
        CancellationToken ct = default)
        => throw new NotSupportedException("This store does not support scientific validation reports.");
    Task<IReadOnlyList<ScientificValidationReport>> ListScientificValidationReportsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScientificValidationReport>>([]);

    Task<InvestigationCase> SaveInvestigationAsync(InvestigationCase value, CancellationToken ct = default);
    Task<InvestigationCase?> GetInvestigationAsync(Guid investigationId, CancellationToken ct = default);
    Task<IReadOnlyList<InvestigationCase>> ListInvestigationsAsync(CancellationToken ct = default);
    Task<PossibleCause> SaveCauseAsync(PossibleCause value, CancellationToken ct = default);
    Task<PossibleCause?> GetCauseAsync(Guid causeId, CancellationToken ct = default);
    Task<IReadOnlyList<PossibleCause>> ListCausesAsync(Guid investigationId, CancellationToken ct = default);
    Task<ProcessTrial> SaveTrialAsync(ProcessTrial value, CancellationToken ct = default);
    Task<ProcessTrial?> GetTrialAsync(Guid trialId, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessTrial>> ListTrialsAsync(Guid investigationId, CancellationToken ct = default);
    Task<TrialResult> AddTrialResultAsync(TrialResult value, CancellationToken ct = default);
    Task<IReadOnlyList<TrialResult>> ListTrialResultsAsync(Guid trialId, CancellationToken ct = default);
    Task<InvestigationConclusion> AddConclusionAsync(
        InvestigationConclusion value,
        CancellationToken ct = default);
    Task<InvestigationConclusion?> GetConclusionAsync(Guid conclusionId, CancellationToken ct = default);
    Task<IReadOnlyList<InvestigationConclusion>> ListConclusionsAsync(
        Guid investigationId,
        CancellationToken ct = default);

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
    Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default);
    Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default);
    Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default);

    Task<ParameterRecommendation> SaveRecommendationAsync(
        ParameterRecommendation value,
        CancellationToken ct = default);
    Task<ParameterRecommendation?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ParameterRecommendation>> ListRecommendationsAsync(CancellationToken ct = default);

    Task AddAuditEntryAsync(ImprovementAuditEntry value, CancellationToken ct = default);
    Task<IReadOnlyList<ImprovementAuditEntry>> ListAuditEntriesAsync(
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
