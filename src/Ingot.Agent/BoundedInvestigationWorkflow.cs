using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Ingot.Agent;

/// <summary>
///     有界群聊工作流。参与者只读取同一批已经验证的工具结果，不能自行调用工具，
///     协调器负责轮数、消息预算、证据归一化和确定性终止。
/// </summary>
public sealed partial class BoundedInvestigationWorkflow(IOptions<ChatOptions> options) : IInvestigationWorkflow
{
    private readonly ChatOptions _options = options.Value;

    public async Task<InvestigationWorkflowResult> RunAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        IModelClient model,
        Func<string, object?, CancellationToken, Task> publish,
        CancellationToken ct = default)
    {
        var maxRounds = Math.Clamp(_options.MaxDiscussionRounds, 1, 5);
        var maxTurns = Math.Clamp(_options.MaxDiscussionTurns, 3, 15);
        var evidence = results.SelectMany(static result => result.Evidence)
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        var task = new InvestigationTask
        {
            Question = request.Question,
            Scope = request.PageContext is null
                ? "当前用户有权访问的分析范围"
                : $"{request.PageContext.Kind}:{request.PageContext.Id}",
            MaxRounds = maxRounds
        };
        await publish(AgentStreamEventTypes.DiscussionStarted,
            new { task, roles = InvestigationRoles.All, maxTurns }, ct).ConfigureAwait(false);

        var transcript = new List<InvestigationContribution>();
        var hypotheses = new List<InvestigationHypothesis>();
        var claims = new List<EvidenceClaim>();
        var modelCalls = new List<ModelCallUsage>();
        var limitations = new List<string>
        {
            "深度分析的多角色讨论只生成候选解释，不能替代确定性统计检验或工程师确认。",
            "参与者只能读取本次已验证工具结果，不能自行访问数据库、网络或设备。"
        };
        var turns = 0;

        for (var round = 1; round <= maxRounds && turns < maxTurns; round++)
        {
            var roles = InvestigationRoles.All.Take(maxTurns - turns).ToArray();
            var roleTasks = roles.Select(role => RunParticipantAsync(
                model,
                new InvestigationTurn
                {
                    Role = role,
                    Round = round,
                    Task = task,
                    Request = request,
                    Plan = plan,
                    ToolResults = results,
                    Hypotheses = hypotheses.ToArray(),
                    Claims = claims.ToArray()
                }, ct)).ToArray();
            var participantResults = await Task.WhenAll(roleTasks).ConfigureAwait(false);
            foreach (var failed in participantResults.Where(static item => item.ErrorType is not null))
            {
                limitations.Add($"{failed.Role} 在第 {round} 轮不可用，已从本轮讨论中隔离。");
                await publish(AgentStreamEventTypes.DiscussionParticipantFailed,
                    new { role = failed.Role, round, errorType = failed.ErrorType }, ct).ConfigureAwait(false);
            }

            var successful = participantResults.Where(static item => item.Result is not null).ToArray();
            modelCalls.AddRange(successful.Select(static item => item.Result!.Usage));
            if (round == 1 && successful.Length < 2)
            {
                limitations.Add("第一轮少于两个角色成功返回，无法形成有交叉审查的候选解释。");
                break;
            }
            if (successful.Length == 0)
                break;

            var normalizedThisRound = new List<InvestigationContribution>();
            foreach (var participant in successful)
            {
                ct.ThrowIfCancellationRequested();
                var result = participant.Result!;
                var contribution = Normalize(
                    result.Value,
                    participant.Role,
                    round,
                    evidence,
                    hypotheses,
                    results);
                normalizedThisRound.Add(contribution);
                transcript.Add(contribution);
                foreach (var hypothesis in contribution.Hypotheses)
                {
                    if (hypotheses.All(item => !string.Equals(
                            item.HypothesisId,
                            hypothesis.HypothesisId,
                            StringComparison.Ordinal)))
                        hypotheses.Add(hypothesis);
                }
                claims.AddRange(contribution.Claims);
                turns++;
                await publish(AgentStreamEventTypes.DiscussionMessage, contribution, ct)
                    .ConfigureAwait(false);
            }

            // 第一轮必须形成候选假设；后续轮次若所有角色都没有新增主张，确定性终止。
            if (round == 1 && hypotheses.Count == 0)
                break;
            if (round > 1 && normalizedThisRound.All(item => item.Claims.Count == 0 && item.Hypotheses.Count == 0))
                break;
        }

        var supported = hypotheses.Count(hypothesis =>
            claims.Any(claim => claim.HypothesisId == hypothesis.HypothesisId &&
                                claim.Position == EvidenceClaimPositions.Support) &&
            claims.All(claim => claim.HypothesisId != hypothesis.HypothesisId ||
                                claim.Position != EvidenceClaimPositions.Oppose));
        var participantQuorumReached = transcript.Where(static item => item.Round == 1)
            .Select(static item => item.Role)
            .Distinct(StringComparer.Ordinal)
            .Count() >= 2;
        var verdict = new InvestigationVerdict
        {
            Status = !participantQuorumReached || hypotheses.Count == 0 ? "insufficient-data" : "candidate",
            Summary = !participantQuorumReached
                ? "参与调查的角色不足，无法形成经过交叉审查的候选解释。"
                : hypotheses.Count == 0
                ? "现有只读证据不足以形成可审查的调查假设。"
                : supported > 0
                    ? "形成了有证据支持的候选解释，但尚未证明因果关系。"
                    : "形成了待验证假设，但现有证据不足以确定主要原因。",
            Hypotheses = hypotheses,
            Claims = claims,
            Transcript = transcript,
            Evidence = evidence,
            Limitations = limitations.Distinct(StringComparer.Ordinal).ToArray()
        };
        await publish(AgentStreamEventTypes.DiscussionCompleted, verdict, ct).ConfigureAwait(false);
        return new InvestigationWorkflowResult { Verdict = verdict, ModelCalls = modelCalls };
    }

    private static async Task<ParticipantResult> RunParticipantAsync(
        IModelClient model,
        InvestigationTurn turn,
        CancellationToken ct)
    {
        try
        {
            return new ParticipantResult(turn.Role, await model.ParticipateAsync(turn, ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ParticipantResult(turn.Role, null, ex.GetType().Name);
        }
    }

    private static InvestigationContribution Normalize(
        InvestigationContribution raw,
        string expectedRole,
        int expectedRound,
        IReadOnlyList<EvidenceRef> allowedEvidence,
        IReadOnlyList<InvestigationHypothesis> existingHypotheses,
        IReadOnlyList<AnalysisToolResult> toolResults)
    {
        var groundedFacts = string.Join('\n', toolResults.Select(static item => $"{item.Summary}\n{item.Data.GetRawText()}"));
        var allowed = allowedEvidence.ToDictionary(static item => (item.Kind, item.Id));
        var knownHypothesisIds = existingHypotheses.Select(static item => item.HypothesisId)
            .ToHashSet(StringComparer.Ordinal);
        var hypotheses = raw.Hypotheses
            .Where(item => IsSafeStatement(item.Statement) && IsSafeStatement(item.Rationale) &&
                           HasGroundedNumbers(item.Statement, groundedFacts) &&
                           HasGroundedNumbers(item.Rationale, groundedFacts))
            .Take(3)
            .Select((item, index) => item with
            {
                HypothesisId = string.IsNullOrWhiteSpace(item.HypothesisId)
                    ? $"h-{expectedRound}-{expectedRole}-{index + 1}"
                    : item.HypothesisId.Trim()[..Math.Min(item.HypothesisId.Trim().Length, 100)],
                AuthorRole = expectedRole,
                Statement = Limit(item.Statement, 500),
                Rationale = Limit(item.Rationale, 1000),
                Evidence = CanonicalEvidence(item.Evidence, allowed)
            })
            .Where(static item => item.Evidence.Count > 0)
            .ToArray();
        foreach (var hypothesis in hypotheses)
            knownHypothesisIds.Add(hypothesis.HypothesisId);

        var claims = raw.Claims
            .Where(item => knownHypothesisIds.Contains(item.HypothesisId) && IsSafeStatement(item.Statement) &&
                           HasGroundedNumbers(item.Statement, groundedFacts))
            .Where(item => item.Position is EvidenceClaimPositions.Support or
                EvidenceClaimPositions.Oppose or EvidenceClaimPositions.Uncertain)
            .Take(10)
            .Select(item => item with
            {
                AuthorRole = expectedRole,
                Statement = Limit(item.Statement, 700),
                Evidence = CanonicalEvidence(item.Evidence, allowed)
            })
            .Where(static item => item.Evidence.Count > 0)
            .ToArray();

        return new InvestigationContribution
        {
            Role = expectedRole,
            Round = expectedRound,
            Summary = Limit(raw.Summary, 500),
            Hypotheses = hypotheses,
            Claims = claims
        };
    }

    private static IReadOnlyList<EvidenceRef> CanonicalEvidence(
        IEnumerable<EvidenceRef> requested,
        IReadOnlyDictionary<(string Kind, string Id), EvidenceRef> allowed)
        => requested.Select(item => allowed.GetValueOrDefault((item.Kind, item.Id)))
            .Where(static item => item is not null)
            .Cast<EvidenceRef>()
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();

    private static bool IsSafeStatement(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !CausalLanguage().IsMatch(value);

    private static bool HasGroundedNumbers(string? value, string groundedFacts)
        => string.IsNullOrWhiteSpace(value) || Number().Matches(value).All(match => groundedFacts.Contains(match.Value, StringComparison.Ordinal));

    [GeneratedRegex(@"确定原因|已证明因果|直接导致|confirmed\s+(the\s+)?root\s+cause|directly\s+caused|proves?\s+causation", RegexOptions.IgnoreCase)]
    private static partial Regex CausalLanguage();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[-+]?\d+(?:[.,]\d+)?%?(?![\p{L}\p{N}])")]
    private static partial Regex Number();

    private static string Limit(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private sealed record ParticipantResult(
        string Role,
        ModelCallResult<InvestigationContribution>? Result,
        string? ErrorType);
}
