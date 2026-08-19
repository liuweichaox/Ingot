using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed class ResearchExperimentCommandStoreAdapter(IProcessResearchStore store)
    : IResearchExperimentCommandStore
{
    public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => store.GetProjectAsync(projectId, ct);

    public Task<ResearchHypothesis?> GetHypothesisAsync(
        Guid hypothesisId,
        CancellationToken ct = default)
        => store.GetHypothesisAsync(hypothesisId, ct);

    public Task<ResearchExperiment?> GetExperimentAsync(
        Guid experimentId,
        CancellationToken ct = default)
        => store.GetExperimentAsync(experimentId, ct);

    public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => store.ListExperimentsAsync(projectId, ct);

    public Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => store.ListExperimentResultsAsync(projectId, ct);

    public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default)
        => store.GetOperatingRegionAsync(operatingRegionId, ct);

    public Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
        => store.SaveExperimentTransactionAsync(updatedExperiment, audit, ct);

    public Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
        => store.SaveControlledDecisionTransactionAsync(updatedExperiment, audit, ct);
}
