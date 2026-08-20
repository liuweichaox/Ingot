// 在宿主启动阶段初始化 InspectionStore 所需的持久化资源。

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionStoreInitializerHostedService(
    IInspectionRecordStore records,
    IInspectionAttachmentStore attachments,
    IInspectionMasterDataStore masterData,
    IInspectionReviewStore reviews) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.WhenAll(
            records.InitializeAsync(cancellationToken),
            attachments.InitializeAsync(cancellationToken),
            masterData.InitializeAsync(cancellationToken),
            reviews.InitializeAsync(cancellationToken));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
