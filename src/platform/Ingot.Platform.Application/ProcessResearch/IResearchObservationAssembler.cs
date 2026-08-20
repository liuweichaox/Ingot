using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed record ResearchObservationAssembly(
    IReadOnlyList<ExperimentRunObservation> Observations,
    int CandidateRunCount)
{
    public int ValidObservationCount =>
        Observations.Count(static value => value.ValidForOptimization);
}

/// <summary>从生产执行和检验事实装配不可变、可追溯的研发观测。</summary>
public interface IResearchObservationAssembler
{
    Task<ResearchObservationAssembly> AssembleAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        CancellationToken ct = default);
}
