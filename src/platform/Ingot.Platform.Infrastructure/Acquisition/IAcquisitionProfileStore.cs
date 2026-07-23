using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Infrastructure.Acquisition;

public interface IAcquisitionProfileStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AcquisitionProfile>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AcquisitionProfile>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<AcquisitionProfile?> GetAsync(string profileId, int version, CancellationToken ct = default);
    Task<AcquisitionProfile> UpsertAsync(AcquisitionProfile value, CancellationToken ct = default);
    Task<bool> DeleteAsync(string profileId, int version, CancellationToken ct = default);
}
