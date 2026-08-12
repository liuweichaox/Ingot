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
