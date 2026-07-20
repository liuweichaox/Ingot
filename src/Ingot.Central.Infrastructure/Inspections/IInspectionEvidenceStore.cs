using Ingot.Contracts.Inspections;

namespace Ingot.Central.Infrastructure.Inspections;

public interface IInspectionEvidenceStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<EvidenceUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        CancellationToken ct = default);

    Task<InspectionEvidenceRef?> GetAsync(Guid evidenceId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid evidenceId, CancellationToken ct = default);
}

