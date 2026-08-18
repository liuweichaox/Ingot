namespace Ingot.Contracts.Events;

/// <summary>Edge 与 Platform 共同编译的事件 HTTP 边界。</summary>
public static class PlatformEventRoutes
{
    public const string BatchIngest = "api/v1/events:batch";
    public const string AbsoluteBatchIngest = "/" + BatchIngest;
}
