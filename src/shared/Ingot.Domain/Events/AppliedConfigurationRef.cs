namespace Ingot.Domain.Events;

public sealed record AppliedConfigurationRef(
    string Kind,
    string Id,
    int Version);
