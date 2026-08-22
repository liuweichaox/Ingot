// 定义检验附件持久化和原始内容读取边界。
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>保存经过格式校验且绑定站点的检验附件。</summary>
public interface IInspectionAttachmentStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<AttachmentUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        string siteId,
        CancellationToken ct = default);

    Task<InspectionAttachment?> GetAsync(Guid attachmentId, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(Guid attachmentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid attachmentId, CancellationToken ct = default);
}
