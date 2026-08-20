using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>向研发资产模块投影最小且不可变的项目上下文。</summary>
public sealed class ResearchProjectContextReader(IProcessResearchStore store) : IResearchProjectContextReader
{
    public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => store.GetProjectAsync(projectId, ct);
}
