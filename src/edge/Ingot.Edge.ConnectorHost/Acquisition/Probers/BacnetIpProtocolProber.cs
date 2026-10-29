// Browses a bounded BACnet Object_List and previews only Present_Value via configured unicast ReadProperty calls.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class BacnetIpProtocolProber(
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<BacnetIpProtocolProber> logger) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.BacnetIp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken cancellationToken)
    {
        var connection = deployment.Task.BacnetIp
            ?? throw new InvalidOperationException("BACnet/IP 连接配置不能为空。");
        using var session = await BacnetIpProtocolSupport.OpenAsync(
            connection,
            egressPolicy,
            deployment.Task.Execution.TimeoutMs,
            logger,
            cancellationToken).ConfigureAwait(false);
        await session.VerifyDeviceIdentifierAsync(cancellationToken).ConfigureAwait(false);
        var objectList = await session.ReadObjectListAsync(cancellationToken).ConfigureAwait(false);
        var directoryPoints = BacnetIpProtocolSupport.BuildDirectoryPoints(objectList);
        var page = AcquisitionProbeSupport.ApplyDiscoveryQuery(directoryPoints, discovery);
        var configuredSelectors = new Dictionary<string, BacnetSelectedObject>(StringComparer.Ordinal);

        if (!AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
        {
            var directory = objectList.ToHashSet();
            foreach (var selector in BacnetIpProtocolSupport.BuildSelectors(deployment))
            {
                if (directory.Any(item => item.Type == selector.ObjectId.Type && item.Instance == selector.ObjectId.Instance))
                    configuredSelectors.TryAdd(selector.SourcePath, selector);
            }
        }

        // Discovery browses object identities only; selected Present_Value properties are read on the configured mappings.
        var values = await session.ReadSnapshotAsync(configuredSelectors.Values.ToArray(), cancellationToken)
            .ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        var previewPoints = page.Points
            .Select(point => values.TryGetValue(point.Path, out var rawValue)
                ? point with
                {
                    DataType = rawValue?.GetType().Name ?? "null",
                    RawValue = AcquisitionProbeSupport.Format(rawValue)
                }
                : point)
            .ToArray();

        return new ProbeSnapshot(
            values,
            directoryPoints,
            mappingsValidated,
            new DiscoveryPage(previewPoints, page.NextCursor));
    }
}
