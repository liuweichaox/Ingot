using Ingot.Platform.Application.ResearchAssets;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed class ResearchExperimentKnowledgeGate(
    IProcessResearchStore store,
    IMechanismKnowledgeStore mechanismKnowledgeStore,
    IResearchAssetStore? researchAssetStore = null) : IResearchExperimentKnowledgeGate
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
        if (researchAssetStore is null)
        {
            if (experiment.Optimization.MechanismModelSnapshotHash != "none")
                throw new ProcessResearchRuleException("无法校验机理模型快照，按失败关闭处理。");
            return;
        }
        var mechanismModels = MechanismModelExperimentPolicy.Select(
            project,
            await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
            await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));
        if (!string.Equals(
                experiment.Optimization.MechanismModelSnapshotHash,
                mechanismModels.SnapshotHash,
                StringComparison.Ordinal))
            throw new ProcessResearchRuleException(
                "机理模型或融合定义已发生变化，请取消当前实验并重新生成计划。");
    }
}
