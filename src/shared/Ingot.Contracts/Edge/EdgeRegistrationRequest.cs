// 定义绑定站点且不可跨站迁移的边缘节点注册契约。
namespace Ingot.Contracts.Edge;

public sealed record EdgeRegistrationRequest
{
    public required string SiteId { get; init; }

    public required string EdgeId { get; init; }

    public string? HostBaseUrl { get; init; }

    public string? Hostname { get; init; }

    public string? Version { get; init; }
}
