// 约束分析文本中的因果措辞和不受证据支持的结论表达。
using System.Text.RegularExpressions;

namespace Ingot.Contracts.Agents;

public static partial class AnalysisTextPolicy
{
    public static bool ContainsUnsupportedCausalClaim(string? value)
        => !string.IsNullOrWhiteSpace(value) && UnsupportedCausalLanguage().IsMatch(value);

    public static IEnumerable<string> EnumerateAnswerText(AnalysisAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        yield return answer.Summary;
        foreach (var finding in answer.Findings)
            yield return finding.Statement;
        foreach (var limitation in answer.Limitations)
            yield return limitation;
        foreach (var question in answer.FollowUpQuestions)
            yield return question;
        foreach (var proposal in answer.Proposals)
        {
            yield return proposal.Title;
            yield return proposal.Rationale;
            foreach (var value in proposal.DraftFields.Values)
                yield return value;
        }
        foreach (var chart in answer.Charts)
        {
            yield return chart.Title;
            foreach (var label in chart.Labels)
                yield return label;
            foreach (var series in chart.Series)
                yield return series.Name;
        }
        if (answer.CombinedAnalysis is not null)
        {
            foreach (var value in EnumerateCombinedAnalysisText(answer.CombinedAnalysis))
                yield return value;
        }
    }

    public static IEnumerable<string> EnumerateCombinedAnalysisText(CombinedAnalysisResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        yield return value.Summary;
        foreach (var limitation in value.Limitations)
            yield return limitation;
        foreach (var cause in value.PossibleCauses)
        {
            yield return cause.Statement;
            yield return cause.Reason;
        }
        foreach (var review in value.Reviews)
            yield return review.Statement;
        foreach (var step in value.ReviewSteps)
        {
            yield return step.Summary;
            foreach (var cause in step.PossibleCauses)
            {
                yield return cause.Statement;
                yield return cause.Reason;
            }
            foreach (var review in step.Reviews)
                yield return review.Statement;
        }
    }

    [GeneratedRegex(
        @"(?:确定原因|(?:已)?证明(?:了)?因果|直接导致|(?<!不)(?<!未)(?<!无法)(?:导致|造成|引发)|根因(?:是|为)|归因于|源于|confirmed\s+(?:the\s+)?root\s+cause|proven\s+cause|proves?\s+causation|root\s+cause\s+(?:is|was)|directly\s+caused|caused\s+by|leads?\s+to|resulted?\s+in|responsible\s+for)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedCausalLanguage();
}
