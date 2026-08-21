using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed record ResearchObservationAssembly(
    IReadOnlyList<ExperimentRunObservation> Observations,
    int CandidateRunCount)
{
    public int ValidObservationCount =>
        Observations.Count(static value => value.ValidForOptimization);
}

public interface IResearchObservationAssembler
{
    Task<ResearchObservationAssembly> AssembleAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        CancellationToken ct = default);
}
