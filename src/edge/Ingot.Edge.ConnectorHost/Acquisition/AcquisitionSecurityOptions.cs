// 定义 Edge 采集凭据与网络目标的显式许可配置。
namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionSecurityOptions
{
    public string[] AllowedSecretEnvironmentVariables { get; set; } = [];

    public string[] AllowedHttpHosts { get; set; } = [];

    public string[] AllowedNetworkHosts { get; set; } = [];

    public bool AllowPrivateNetworkHttpTargets { get; set; }

    public bool AllowPrivateNetworkTargets { get; set; }
}
