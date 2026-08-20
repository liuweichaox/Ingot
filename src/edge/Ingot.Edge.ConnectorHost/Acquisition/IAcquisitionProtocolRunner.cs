using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>按协议执行设备探查和采集读取，不向核心领域暴露协议实现细节。</summary>
public interface IAcquisitionProtocolRunner
{
    string Protocol { get; }

    Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct);
}

/// <summary>为采集协议解析受保护的连接凭据，调用方不得持久化解析后的明文。</summary>
public interface IAcquisitionSecretResolver
{
    string? Resolve(string? reference);
}

/// <summary>
/// Resolves edge-local environment references such as env:MQTT_PASSWORD.
/// Secret values never enter the platform ingestion task or its API responses.
/// </summary>
public sealed class EnvironmentAcquisitionSecretResolver : IAcquisitionSecretResolver
{
    public string? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        const string prefix = "env:";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("采集凭据引用必须使用 env:变量名 格式。");
        var name = reference[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("采集凭据引用缺少环境变量名。");
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"采集凭据引用 {reference} 在边缘节点上不存在。");
    }
}
