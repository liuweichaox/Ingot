namespace Ingot.Contracts.Events;

public static class PlatformEventRoutes
{
    public const string BatchIngest = "api/v1/events:batch";
    public const string AbsoluteBatchIngest = "/" + BatchIngest;
}
