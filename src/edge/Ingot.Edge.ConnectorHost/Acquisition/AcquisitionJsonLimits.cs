// 实现边缘采集组件 AcquisitionJsonLimits，保持协议解析、凭据和领域事件边界分离。

using System.Text.Json;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class AcquisitionJsonLimits
{
    public const int MaximumPayloadBytes = 16 * 1024 * 1024;

    public static JsonDocumentOptions DocumentOptions => new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };
}
