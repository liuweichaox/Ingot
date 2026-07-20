using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionAttachmentStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<AttachmentUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        CancellationToken ct = default);

    Task<InspectionAttachment?> GetAsync(Guid attachmentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid attachmentId, CancellationToken ct = default);
}

