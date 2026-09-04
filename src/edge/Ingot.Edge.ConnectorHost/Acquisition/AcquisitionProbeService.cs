// 对已授权采集配置执行有界探查，并返回可发布前审阅的设备点位。
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition.Probers;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionProbeService(IEnumerable<IProtocolProber> probers)
{
    private readonly IReadOnlyDictionary<string, IProtocolProber> _probers =
        probers.ToDictionary(static item => item.Protocol, StringComparer.Ordinal);

    public async Task<AcquisitionProbeResult> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery? discovery,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
            deployment.Task.Execution.TimeoutMs,
            500,
            30_000));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var ct = timeoutSource.Token;

        discovery ??= new SourceDiscoveryQuery();
        if (!_probers.TryGetValue(deployment.Task.Protocol, out var prober))
            throw new InvalidOperationException($"不支持采集协议 {deployment.Task.Protocol}。");
        var raw = await prober.ProbeAsync(deployment, discovery, ct).ConfigureAwait(false);

        var page = raw.Page ?? AcquisitionProbeSupport.ApplyDiscoveryQuery(raw.Points, discovery);
        var previews = AcquisitionProbeSupport.BuildPreviews(deployment, raw.Values, raw.TopicValues);
        var missing = previews.Where(static item => !item.Accepted).ToArray();
        var unlocated = AcquisitionProbeSupport.PublicationEvidencePaths(deployment)
            .Where(path => !AcquisitionProbeSupport.EvidenceLocated(
                deployment.Task.Protocol, path, raw.Values, raw.TopicValues))
            .ToArray();
        var warnings = unlocated
            .Select(path => $"已配置设备路径未在探查样本中出现：{path.Display}。")
            .ToArray();
        var allMappingsLocated = unlocated.Length == 0;
        return new AcquisitionProbeResult
        {
            Success = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated,
            MappingsValidated = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated,
            Protocol = deployment.Task.Protocol,
            TestedAt = DateTimeOffset.UtcNow,
            Message = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated
                ? $"连接成功，读取到 {raw.Points.Count} 个设备点位，映射验证通过。"
                : missing.Length > 0
                    ? $"连接成功，但有 {missing.Length} 个必需映射未读取到值。"
                    : !allMappingsLocated
                        ? $"连接成功，但有 {unlocated.Length} 个已配置设备路径未在探查样本中出现。"
                    : "连接成功，但设备报文未通过映射验证。",
            Points = page.Points,
            NextCursor = page.NextCursor,
            ScannedPointCount = raw.Points.Count,
            ScanLimitReached = raw.Points.Count >= AcquisitionProbeSupport.MaximumPoints,
            Mappings = previews,
            Warnings = warnings
        };
    }
}
