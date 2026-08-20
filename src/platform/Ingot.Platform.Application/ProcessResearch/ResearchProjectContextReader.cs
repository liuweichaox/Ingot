using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed class ResearchProjectContextReader(IProcessResearchStore store) : IResearchProjectContextReader
{
    public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => store.GetProjectAsync(projectId, ct);
}
