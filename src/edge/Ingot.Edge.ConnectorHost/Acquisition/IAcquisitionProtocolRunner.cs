// 定义各采集协议运行器和密钥解析器共用的 Edge 内部边界。
using Ingot.Contracts.Acquisition;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>在统一生命周期和安全策略内运行一种采集协议。</summary>
public interface IAcquisitionProtocolRunner
{
    string Protocol { get; }

    Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct);
}

/// <summary>仅从 Edge 允许的秘密来源解析采集凭据。</summary>
public interface IAcquisitionSecretResolver
{
    string? Resolve(string? reference);
}

public sealed class EnvironmentAcquisitionSecretResolver(
    IOptions<AcquisitionSecurityOptions> options) : IAcquisitionSecretResolver
{
    private readonly HashSet<string> _allowedEnvironmentVariables =
        (options.Value.AllowedSecretEnvironmentVariables ?? Array.Empty<string>())
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value.Trim())
        .ToHashSet(StringComparer.Ordinal);

    public string? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        if (!AcquisitionSecretReferencePolicy.TryParseEnvironmentReference(reference, out var name, out var error))
            throw new InvalidOperationException(error);
        if (!_allowedEnvironmentVariables.Contains(name))
            throw new InvalidOperationException($"采集凭据引用 {reference} 未列入 Edge 密钥允许清单。");
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"采集凭据引用 {reference} 在边缘节点上不存在。");
    }
}
