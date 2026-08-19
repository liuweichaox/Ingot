using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed partial class ProcessResearchWorkflow(
    IProcessResearchStore store,
    ResearchExperimentCommands experimentCommands,
    IProcessConfigurationStore? processConfigurations = null,
    IMechanismKnowledgeStore? mechanismKnowledgeStore = null)
{
    internal ResearchExperimentCommands ExperimentCommands { get; } = experimentCommands;

    private async Task<ResearchProject> RequireProjectAsync(Guid projectId, CancellationToken ct)
        => await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
           ?? throw new ProcessResearchRuleException("研发项目不存在。");

    private async Task<ResearchProject> RequireMutableProjectAsync(
        Guid projectId,
        CancellationToken ct)
    {
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        return project;
    }

    private async Task AuditAsync(
        Guid projectId,
        string resourceType,
        string resourceId,
        string action,
        string userId,
        string? fromStatus,
        string? toStatus,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = projectId,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = NormalizeUser(userId),
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

    [GeneratedRegex("^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
