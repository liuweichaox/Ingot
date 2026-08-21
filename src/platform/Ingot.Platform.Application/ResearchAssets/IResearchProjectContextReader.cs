using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ResearchAssets;

public interface IResearchProjectContextReader
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
}
