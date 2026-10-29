// Limits MTConnect requests to declared DataItem mappings and shared selector identities.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition.Probers;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class MtConnectAcquisitionSupport
{
    public const int MaximumConfiguredDataItemIds = 128;

    public static IReadOnlyList<string> ConfiguredDataItemIds(AcquisitionDeployment deployment)
    {
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment)) return [];
        var task = deployment.Task;
        IEnumerable<string?> ids = task.ValueMappings.Select(static mapping => mapping.SourcePath)
            .Concat(task.ContextMappings.Select(static mapping => mapping.SourcePath));
        if (task.ProcessSpecification is { } specification)
        {
            ids = ids.Append(specification.IdPath)
                .Append(specification.VersionPath)
                .Append(specification.NamePath)
                .Concat(specification.ParameterMappings.Select(static mapping => mapping.SourcePath));
        }
        var configured = ids.Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (configured.Any(static id => id.Length > 512 || id.Any(char.IsControl)))
            throw new InvalidDataException("MTConnect DataItem ID 超过长度上限或包含控制字符。");
        if (configured.Length > MaximumConfiguredDataItemIds)
            throw new InvalidDataException(
                $"MTConnect 每个采集任务最多读取 {MaximumConfiguredDataItemIds} 个不同 DataItem ID。");
        return configured;
    }

    public static void ValidateTimestampMode(IngestionTask task)
    {
        if (task.TimestampMode != "edge-received" || !string.IsNullOrWhiteSpace(task.SequencePath))
            throw new InvalidDataException(
                "MTConnect 首期统一使用 Edge 接收时间；每个观察值具有独立来源时间和 sequence，不支持任务级来源时间戳或 sequence 路径。");
    }
}
