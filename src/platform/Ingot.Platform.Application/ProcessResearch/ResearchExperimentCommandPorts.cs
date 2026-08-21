using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public interface IResearchExperimentCommandStore
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default);
    Task<ResearchExperiment?> GetExperimentAsync(Guid experimentId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
}

public interface IResearchExperimentPlanValidator
{
    Task<ResearchExperimentValidationResult> ValidateAsync(
        Guid projectId,
        ResearchExperiment request,
        CancellationToken ct = default);
}

public interface IResearchOnlineAdmissionGate
{
    Task<ResearchOnlineAdmissionEvidence> RequireAsync(
        Guid projectId,
        string mechanismKnowledgeSnapshotHash,
        CancellationToken ct = default);
}

public interface IResearchExperimentKnowledgeGate
{
    Task ValidateAsync(ResearchExperiment experiment, CancellationToken ct = default);
}

public sealed class ProcessResearchRuleException(string message)
    : InvalidOperationException(message);

public sealed class ResearchExperimentValidationException(
    IReadOnlyList<ResearchExperimentValidationIssue> errors)
    : InvalidOperationException(errors.FirstOrDefault()?.Message ?? "实验计划未通过校验。")
{
    public IReadOnlyList<ResearchExperimentValidationIssue> Errors { get; } = errors;
}
