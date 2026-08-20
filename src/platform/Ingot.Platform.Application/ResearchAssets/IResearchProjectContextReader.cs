using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>
///     Supplies the minimum project context required to validate research assets.
///     The port keeps ResearchAssets independent from the ProcessResearch module's store.
/// </summary>
public interface IResearchProjectContextReader
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
}
