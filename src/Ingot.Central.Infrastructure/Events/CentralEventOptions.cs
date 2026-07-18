namespace Ingot.Central.Infrastructure.Events;

public sealed class CentralEventOptions
{
    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
