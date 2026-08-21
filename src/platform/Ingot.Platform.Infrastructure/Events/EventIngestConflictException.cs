namespace Ingot.Platform.Infrastructure.Events;

public sealed class EventIngestConflictException(string message) : Exception(message);
