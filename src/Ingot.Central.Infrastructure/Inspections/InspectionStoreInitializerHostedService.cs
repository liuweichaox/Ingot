namespace Ingot.Central.Infrastructure.Inspections;

public sealed class InspectionStoreInitializerHostedService(
    IInspectionRecordStore records,
    IInspectionEvidenceStore evidence,
    IInspectionMasterDataStore masterData) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => Task.WhenAll(
            records.InitializeAsync(cancellationToken),
            evidence.InitializeAsync(cancellationToken),
            masterData.InitializeAsync(cancellationToken));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

