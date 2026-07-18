namespace Ingot.Application.Abstractions;

/// <summary>
///     提供当前 Edge 节点的稳定标识。
///     实现由宿主决定（配置、文件或环境），基础设施实现只依赖本抽象。
/// </summary>
public interface IEdgeIdentityProvider
{
    string GetEdgeId();
}
