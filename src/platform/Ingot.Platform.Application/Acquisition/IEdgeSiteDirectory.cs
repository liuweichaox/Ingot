namespace Ingot.Platform.Application.Acquisition;

/// <summary>Lists only the edge identities owned by a site for cross-resource scope checks.</summary>
public interface IEdgeSiteDirectory
{
    Task<IReadOnlySet<string>> ListEdgeIdsAsync(string siteId, CancellationToken ct = default);
}
