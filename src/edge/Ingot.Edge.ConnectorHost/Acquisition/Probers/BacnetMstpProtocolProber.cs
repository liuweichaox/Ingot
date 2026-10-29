// Browses only the configured BACnet MS/TP device and reads mapped Present_Value objects.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class BacnetMstpProtocolProber(BacnetMstpSessionPool sessionPool) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.BacnetMstp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken cancellationToken)
    {
        var connection = deployment.Task.BacnetMstp
            ?? throw new InvalidOperationException("BACnet MS/TP 连接配置不能为空。");
        await using var session = await sessionPool.AcquireAsync(connection, cancellationToken).ConfigureAwait(false);
        var objectList = await session.ReadObjectListAsync(
            connection.DeviceInstance,
            deployment.Task.Execution.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
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

        var values = await session.ReadSnapshotAsync(
            connection.DeviceInstance,
            configuredSelectors.Values.ToArray(),
            deployment.Task.Execution.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
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
