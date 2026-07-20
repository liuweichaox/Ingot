namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionStoreInitializerHostedService(
    IInspectionRecordStore records,
    IInspectionAttachmentStore attachments,
    IInspectionMasterDataStore masterData) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.WhenAll(
            records.InitializeAsync(cancellationToken),
            attachments.InitializeAsync(cancellationToken),
            masterData.InitializeAsync(cancellationToken));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

