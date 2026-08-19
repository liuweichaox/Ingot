using Ingot.Platform.Application.ResearchAssets;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed class ResearchExperimentKnowledgeGate(
    IProcessResearchStore store,
    IMechanismKnowledgeStore mechanismKnowledgeStore) : IResearchExperimentKnowledgeGate
{
    public async Task ValidateAsync(ResearchExperiment experiment, CancellationToken ct = default)
    {
        if (experiment.Optimization is null)
            return;
        var project = await store.GetProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var knowledge = MechanismKnowledgeExperimentPolicy.Select(
            project,
            await mechanismKnowledgeStore.ListClaimsAsync(project.ProjectId, ct).ConfigureAwait(false),
            await mechanismKnowledgeStore.ListConflictsAsync(project.ProjectId, ct).ConfigureAwait(false));
        var currentHash = MechanismKnowledgeExperimentPolicy.SnapshotHash(knowledge);
        if (!string.Equals(
                experiment.Optimization.MechanismKnowledgeSnapshotHash,
                currentHash,
                StringComparison.Ordinal))
            throw new ProcessResearchRuleException(
                "机理知识已发生变化，请取消当前实验并基于最新知识重新生成计划。");
        MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(experiment, knowledge);
    }
}
