namespace Ingot.Central.Infrastructure.Inspections;

public sealed class InspectionSubmissionOptions
{
    public bool RequireToken { get; set; } = true;

    /// <summary>提交者标识到 Bearer Token 的映射。</summary>
    public Dictionary<string, string> ActorTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
