
namespace Ingot.Contracts.Agents;

public static class GoldenQuestionValidator
{
    public static bool TryValidate(
        GoldenQuestionCase? value,
        bool forReview,
        out GoldenQuestionCase? normalized,
        out string error)
    {
        normalized = null;
        if (value is null || value.Version < 1 || string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.Question))
            return Fail("黄金问题必须包含名称、问题和正版本号。", out error);
        if (!ProductEntryPoints.All.Contains(value.EntryPoint) || value.Mode is not ("quick" or "combined"))
            return Fail("黄金问题的入口或分析模式无效。", out error);
        if (!GoldenQuestionStatuses.IsValid(value.Status))
            return Fail("黄金问题状态无效。", out error);

        if (value.ExpectedFacts.Any(static fact => string.IsNullOrWhiteSpace(fact.FactId) ||
                                                   string.IsNullOrWhiteSpace(fact.Tool) ||
                                                   fact.JsonPointer is null))
            return Fail("预期事实必须包含标识、工具和有效 JSON Pointer。", out error);
        var facts = value.ExpectedFacts.Select(fact => fact with
        {
            FactId = fact.FactId.Trim(),
            Tool = fact.Tool.Trim(),
            JsonPointer = fact.JsonPointer.Trim(),
            AnswerMustContain = NullIfBlank(fact.AnswerMustContain)
        }).ToArray();
        if (facts.Any(static fact =>
                !string.IsNullOrEmpty(fact.JsonPointer) && !fact.JsonPointer.StartsWith('/')))
            return Fail("预期事实必须包含标识、工具和有效 JSON Pointer。", out error);
        if (facts.Select(static fact => fact.FactId).Distinct(StringComparer.Ordinal).Count() != facts.Length)
            return Fail("预期事实标识不得重复。", out error);

        var references = value.ExpectedRecordReferences
            .Where(static item => !string.IsNullOrWhiteSpace(item.Kind) &&
                                  !string.IsNullOrWhiteSpace(item.Id) &&
                                  !string.IsNullOrWhiteSpace(item.Label))
            .Select(static item => item with
            {
                Kind = item.Kind.Trim(),
                Id = item.Id.Trim(),
                Label = item.Label.Trim()
            })
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        if (forReview && !value.ExpectRefusal && facts.Length == 0)
            return Fail("非拒绝类黄金问题至少需要一条可自动核对的事实。", out error);
        if (forReview && references.Length == 0)
            return Fail("审核黄金问题前至少需要一条预期生产记录引用。", out error);

        normalized = value with
        {
            CaseId = value.CaseId == Guid.Empty ? Guid.CreateVersion7() : value.CaseId,
            Name = value.Name.Trim(),
            Question = value.Question.Trim(),
            EntryPoint = value.EntryPoint.Trim(),
            Mode = value.Mode.Trim(),
            ExpectedFacts = facts,
            ExpectedRecordReferences = references,
            ForbiddenAnswerText = value.ForbiddenAnswerText
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        error = string.Empty;
        return true;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
