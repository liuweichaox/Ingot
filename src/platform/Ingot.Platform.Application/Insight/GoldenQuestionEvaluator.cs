using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Insight;

public sealed class GoldenQuestionEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CausalClaims =
    [
        "导致", "已证明因果", "确定原因", "confirmed root cause", "proven cause",
        "directly caused", "caused by"
    ];

    public GoldenQuestionEvaluation Evaluate(GoldenQuestionCase goldenCase, AgentRunSnapshot run)
    {
        if (goldenCase.Status != GoldenQuestionStatuses.Reviewed)
            throw new InvalidOperationException("只有经工艺工程师审核的黄金问题才能执行评测。");

        var gates = new List<GoldenEvaluationGate>();
        Add("run.completed", run.Status == AgentRunStatuses.Completed && run.Answer is not null,
            "Agent 运行必须完成并产生回答。");
        Add("question.matches", string.Equals(goldenCase.Question.Trim(), run.Question.Trim(), StringComparison.Ordinal),
            "运行问题必须与冻结的黄金问题逐字一致。");
        Add("execution.auditable", AuditReady(run),
            "运行必须保存模型、提示词、工具集版本及带 SHA-256 的已验证工具结果。");

        foreach (var fact in goldenCase.ExpectedFacts)
        {
            var toolResult = run.ToolResults.FirstOrDefault(result =>
                string.Equals(result.Tool, fact.Tool, StringComparison.Ordinal));
            var found = toolResult is not null &&
                        TryResolvePointer(toolResult.Data, fact.JsonPointer, out var actual) &&
                        JsonElement.DeepEquals(actual, fact.ExpectedValue);
            Add($"fact.{fact.FactId}.tool", found,
                found ? "工具事实与审核值一致。" : "工具事实缺失或与审核值不一致。");
            if (!string.IsNullOrWhiteSpace(fact.AnswerMustContain))
            {
                Add($"fact.{fact.FactId}.answer", AnswerText(run).Contains(
                        fact.AnswerMustContain,
                        StringComparison.OrdinalIgnoreCase),
                    "回答必须包含工艺工程师审核的事实表述。");
            }
        }

        var answerRefs = run.Answer?.RelatedRecords ?? [];
        foreach (var expected in goldenCase.ExpectedRecordReferences)
        {
            Add($"reference.{expected.Kind}.{expected.Id}", answerRefs.Any(actual =>
                    string.Equals(actual.Kind, expected.Kind, StringComparison.Ordinal) &&
                    string.Equals(actual.Id, expected.Id, StringComparison.Ordinal)),
                "回答必须引用审核指定的原始生产记录。");
        }

        if (goldenCase.ExpectRefusal)
        {
            var insufficientTool = run.ToolResults.Any(static result => result.Outcome == "insufficient-data");
            var refused = run.Answer is { Findings.Count: 0, Charts.Count: 0, CombinedAnalysis: null } answer &&
                          answer.Limitations.Count > 0;
            Add("refusal.correct", insufficientTool && refused,
                "数据不足类问题必须由工具证据触发，并拒绝给出确定结论或图表。");
        }

        var forbidden = CausalClaims.Concat(goldenCase.ForbiddenAnswerText)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hasStructuredCausalClaim = run.Answer is not null &&
            (string.Equals(run.Answer.SummaryStrength, AnalysisClaimStrengths.Causal, StringComparison.Ordinal) ||
             run.Answer.Findings.Any(static finding =>
                 string.Equals(finding.Strength, AnalysisClaimStrengths.Causal, StringComparison.Ordinal)));
        Add("causal-guard", !hasStructuredCausalClaim &&
            !forbidden.Any(term => AnswerText(run).Contains(term, StringComparison.OrdinalIgnoreCase)),
            "未经受控实验验证的回答不得包含因果断言或审核禁用文本。");

        return new GoldenQuestionEvaluation
        {
            EvaluationId = Guid.CreateVersion7(),
            CaseId = goldenCase.CaseId,
            CaseVersion = goldenCase.Version,
            AgentRunId = run.RunId,
            Passed = gates.All(static gate => gate.Passed),
            Gates = gates,
            ModelProvider = run.ModelProvider,
            Model = run.Model,
            PromptVersion = run.PromptVersion,
            ToolsetVersion = run.ToolsetVersion,
            AgentRunSnapshotHash = SnapshotHash(run),
            EvaluatedAt = DateTimeOffset.UtcNow
        };

        void Add(string code, bool passed, string detail)
            => gates.Add(new GoldenEvaluationGate { Code = code, Passed = passed, Detail = detail });
    }

    public static string SnapshotHash(AgentRunSnapshot run)
        => Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(run, JsonOptions)));

    public static bool TryResolvePointer(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrEmpty(pointer))
            return true;
        if (!pointer.StartsWith('/'))
            return false;
        foreach (var rawSegment in pointer.Split('/').Skip(1))
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(segment, out value))
                    return false;
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array &&
                int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }
            return false;
        }
        return true;
    }

    private static bool AuditReady(AgentRunSnapshot run)
        => !string.IsNullOrWhiteSpace(run.ModelProvider) &&
           !string.IsNullOrWhiteSpace(run.Model) &&
           !string.IsNullOrWhiteSpace(run.PromptVersion) &&
           !string.IsNullOrWhiteSpace(run.ToolsetVersion) &&
           run.ToolResults.Count > 0 &&
           run.ToolResults.All(static result => result.ContentHash.Length == 64);

    private static string AnswerText(AgentRunSnapshot run)
        => run.Answer is null ? string.Empty : string.Join('\n', new[] { run.Answer.Summary }
            .Concat(run.Answer.Findings.Select(static finding => finding.Statement))
            .Concat(run.Answer.Limitations));
}
