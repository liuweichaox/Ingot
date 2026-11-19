// 校验知识记录的必填字段、范围和规范化边界。
namespace Ingot.Contracts.ResearchAssets;

public static class ResearchAssetValidator
{
    public static bool TryValidate(KnowledgeRecord? value, out KnowledgeRecord? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("知识记录不能为空。", out error);
        if (value.SourceId == Guid.Empty)
            return Fail("知识来源标识不能为空。", out error);
        if (!TryText(value.Content, "知识内容", 16000, out var content, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        normalized = value with
        {
            RecordId = value.RecordId == Guid.Empty ? Guid.CreateVersion7() : value.RecordId,
            Content = content!,
            CreatedBy = createdBy!,
            Category = NormalizeOptional(value.Category)?.ToLowerInvariant() ?? "field-note",
            PageOrSheet = NormalizeOptional(value.PageOrSheet),
            Region = NormalizeOptional(value.Region),
            StructuredValues = NormalizeMap(value.StructuredValues),
            CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt
        };
        error = null;
        return true;
    }

    private static bool TryText(
        string? value,
        string field,
        int maximumLength,
        out string? normalized,
        out string? error)
    {
        normalized = NormalizeOptional(value);
        if (normalized is null || normalized.Length > maximumLength)
            return Fail($"{field}不能为空且最长 {maximumLength} 个字符。", out error);
        error = null;
        return true;
    }

    private static IReadOnlyDictionary<string, string> NormalizeMap(IReadOnlyDictionary<string, string> values)
        => values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim().ToLowerInvariant(),
                pair => pair.Value.Trim(),
                StringComparer.Ordinal);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
