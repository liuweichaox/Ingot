using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public interface IProcessResearchStore
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ResearchProject?> GetProjectByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(CancellationToken ct = default);
    Task<ResearchProject> SaveProjectAsync(ResearchProject value, CancellationToken ct = default);

    Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default);

    Task<ResearchExperiment?> GetExperimentAsync(Guid experimentId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default);

    Task<ResearchProcessWindow?> GetProcessWindowAsync(Guid windowId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchProcessWindow>> ListProcessWindowsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchProcessWindow> SaveProcessWindowAsync(
        ResearchProcessWindow value,
        CancellationToken ct = default);

    Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(Guid claimId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        ResearchKnowledgeClaim value,
        CancellationToken ct = default);
}

public sealed class ProcessResearchRuleException(string message) : InvalidOperationException(message);
