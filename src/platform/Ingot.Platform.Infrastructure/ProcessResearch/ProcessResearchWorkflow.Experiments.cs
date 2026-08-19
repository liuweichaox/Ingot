using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class ProcessResearchWorkflow
{
    private ResearchExperimentCommands ExperimentCommands => new(
        new ResearchExperimentCommandStoreAdapter(store),
        onlineAdmission,
        experimentValidation,
        mechanismKnowledgeStore is null
            ? null
            : new ResearchExperimentKnowledgeGate(store, mechanismKnowledgeStore));

    public Task<ResearchExperiment> CreateExperimentAsync(
        Guid projectId,
        ResearchExperiment request,
        string userId,
        CancellationToken ct = default)
        => ExecuteExperimentCommandAsync(() => ExperimentCommands.CreateExperimentAsync(
            projectId, request, userId, ct));

    public Task<ResearchExperiment> CloneExperimentAsync(
        Guid experimentId,
        ResearchExperimentCloneRequest request,
        string userId,
        CancellationToken ct = default)
        => ExecuteExperimentCommandAsync(() => ExperimentCommands.CloneExperimentAsync(
            experimentId, request, userId, ct));

    public Task<ResearchExperiment> ChangeExperimentStatusAsync(
        Guid experimentId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
        => ExecuteExperimentCommandAsync(() => ExperimentCommands.ChangeExperimentStatusAsync(
            experimentId, targetStatus, userId, ct));

    public Task<ResearchExperiment> DecideControlledExperimentAsync(
        Guid experimentId,
        ResearchControlledDecisionRequest request,
        string userId,
        CancellationToken ct = default)
        => ExecuteExperimentCommandAsync(() => ExperimentCommands.DecideControlledExperimentAsync(
            experimentId, request, userId, ct));

    private static async Task<ResearchExperiment> ExecuteExperimentCommandAsync(
        Func<Task<ResearchExperiment>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ResearchExperimentPlanValidationException exception)
        {
            throw new ResearchExperimentValidationException(exception.Errors);
        }
        catch (ResearchExperimentCommandException exception)
        {
            throw new ProcessResearchRuleException(exception.Message);
        }
    }

    private static ResearchExperimentExecution BuildExecution(ResearchExperiment experiment)
        => new()
        {
            DispatchId = Guid.CreateVersion7(),
            Commands = experiment.RunPlan.Select(run => new ExperimentExecutionCommand
            {
                CommandId = Guid.CreateVersion7(),
                ExecutionKey = run.ExecutionKey,
                Sequence = run.Sequence,
                BlockKey = run.BlockKey,
                ReplicateKey = run.ReplicateKey,
                RequestedFactors = run.Factors
            }).ToArray()
        };
}
