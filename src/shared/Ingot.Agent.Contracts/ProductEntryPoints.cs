namespace Ingot.Contracts.Agents;

public static class ProductEntryPoints
{
    public const string Chat = "chat";

    public const string Mcp = "mcp";

    public const string Monitor = "monitor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Chat,
        Mcp,
        Monitor
    };
}

public static class RunPurposes
{
    public const string ReadOnlyAnalysis = "read-only-analysis";

    public static string ForEntryPoint(string entryPoint)
        => ProductEntryPoints.All.Contains(entryPoint)
            ? ReadOnlyAnalysis
            : throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, "不支持的功能入口。");
}
