using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>保存检验附件内容及其不可变元数据引用。</summary>
public interface IInspectionAttachmentStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<AttachmentUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        CancellationToken ct = default);

    Task<InspectionAttachment?> GetAsync(Guid attachmentId, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(Guid attachmentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid attachmentId, CancellationToken ct = default);
}
