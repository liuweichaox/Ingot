// 在明确轮次、令牌和并发预算内执行多视角分析并汇总可审计结果。
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Ingot.Agent;

/// <summary>在固定讨论轮次、令牌和并发预算内执行组合分析。</summary>
public sealed partial class BoundedCombinedAnalysisWorkflow(IOptions<ChatOptions> options) : ICombinedAnalysisWorkflow
{
    private readonly ChatOptions _options = options.Value;

    public async Task<CombinedAnalysisWorkflowResult> RunAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        IModelClient model,
        Func<string, object?, CancellationToken, Task> publish,
        CancellationToken ct = default)
    {
        var maxRounds = Math.Clamp(_options.MaxDiscussionRounds, 1, 5);
        var maxTurns = Math.Clamp(_options.MaxDiscussionTurns, 3, 15);
        var relatedRecords = results.SelectMany(static result => result.RelatedRecords)
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        var task = new CombinedAnalysisTask
        {
            Question = request.Question,
            Scope = request.PageContext is null
                ? "当前用户有权访问的分析范围"
                : $"{request.PageContext.Kind}:{request.PageContext.Id}",
            MaxRounds = maxRounds
        };
        await publish(AgentStreamEventTypes.DiscussionStarted,
            new { task, roles = AnalysisPerspectives.All, maxTurns }, ct).ConfigureAwait(false);

        var transcript = new List<PerspectiveAnalysis>();
        var hypotheses = new List<PossibleCause>();
        var claims = new List<FindingReview>();
        var modelCalls = new List<ModelCallUsage>();
        var limitations = new List<string>
        {
            "多视角研判只列出可能原因，不能替代统计检验或工程师确认。",
            "分析过程只能使用本次查询结果，不能自行访问其他数据、网络或设备。"
        };
        var turns = 0;

        for (var round = 1; round <= maxRounds && turns < maxTurns; round++)
        {
            var roles = AnalysisPerspectives.All.Take(maxTurns - turns).ToArray();
            var roleTasks = roles.Select(role => RunParticipantAsync(
                model,
                new CombinedAnalysisTurn
                {
                    Role = role,
                    Round = round,
                    Task = task,
                    Request = request,
                    Plan = plan,
                    ToolResults = results,
                    PossibleCauses = hypotheses.ToArray(),
                    Reviews = claims.ToArray()
                }, ct)).ToArray();
            var participantResults = await Task.WhenAll(roleTasks).ConfigureAwait(false);
            turns += participantResults.Length;
            foreach (var failed in participantResults.Where(static item => item.ErrorType is not null))
            {
                limitations.Add($"{failed.Role} 在本轮不可用，已从讨论中隔离。");
                await publish(AgentStreamEventTypes.DiscussionParticipantFailed,
                    new { role = failed.Role, round, errorType = failed.ErrorType }, ct).ConfigureAwait(false);
            }

            var successful = participantResults.Where(static item => item.Result is not null).ToArray();
            modelCalls.AddRange(successful.Select(static item => item.Result!.Usage));
            if (successful.Length == 0)
                break;

            var normalizedThisRound = new List<PerspectiveAnalysis>();
            foreach (var participant in successful)
            {
                ct.ThrowIfCancellationRequested();
                var result = participant.Result!;
                var contribution = Normalize(
                    result.Value,
                    participant.Role,
                    round,
                    relatedRecords,
                    hypotheses,
                    results);
                if (!DefaultAnalysisResultValidator.TryValidatePerspectiveAnalysis(
                        contribution,
                        results,
                        hypotheses.Select(static item => item.CauseId).ToArray(),
                        out _))
                {
                    limitations.Add($"{participant.Role} 返回的内容未通过证据边界校验，已从讨论中隔离。");
                    await publish(AgentStreamEventTypes.DiscussionParticipantFailed,
                        new { role = participant.Role, round, errorType = "ValidationRejected" }, ct)
                        .ConfigureAwait(false);
                    continue;
                }
                normalizedThisRound.Add(contribution);
                transcript.Add(contribution);
                foreach (var hypothesis in contribution.PossibleCauses)
                {
                    if (hypotheses.All(item => !string.Equals(
                            item.CauseId,
                            hypothesis.CauseId,
                            StringComparison.Ordinal)))
                        hypotheses.Add(hypothesis);
                }
                claims.AddRange(contribution.Reviews);
                await publish(AgentStreamEventTypes.DiscussionMessage, contribution, ct)
                    .ConfigureAwait(false);
            }

            if (round == 1 && normalizedThisRound.Count < 2)
            {
                limitations.Add("第一轮少于两个角色通过证据边界校验，无法形成有交叉审查的可能原因。");
                break;
            }
            if (round == 1 && hypotheses.Count == 0)
                break;
            if (round > 1 && normalizedThisRound.All(item => item.Reviews.Count == 0 && item.PossibleCauses.Count == 0))
                break;
        }

        var supported = hypotheses.Count(hypothesis =>
            claims.Any(claim => claim.CauseId == hypothesis.CauseId &&
                                claim.Position == FindingReviewPositions.Support) &&
            claims.All(claim => claim.CauseId != hypothesis.CauseId ||
                                claim.Position != FindingReviewPositions.Oppose));
        var participantQuorumReached = transcript.Where(static item => item.Round == 1)
            .Select(static item => item.Role)
            .Distinct(StringComparer.Ordinal)
            .Count() >= 2;
        var verdict = new CombinedAnalysisResult
        {
            Status = !participantQuorumReached || hypotheses.Count == 0 ? "insufficient-data" : "needs-review",
            Summary = !participantQuorumReached
                ? "参与调查的角色不足，无法形成经过交叉审查的可能原因。"
                : hypotheses.Count == 0
                ? "现有生产记录不足以列出可复核的可能原因。"
                : supported > 0
                    ? "已经列出有生产数据支持的可能原因，但还不能确认因果关系。"
                    : "已经列出可能原因，但现有生产记录不足以确定主要原因。",
            PossibleCauses = hypotheses,
            Reviews = claims,
            ReviewSteps = transcript,
            RelatedRecords = relatedRecords,
            Limitations = limitations.Distinct(StringComparer.Ordinal).ToArray()
        };
        if (!DefaultAnalysisResultValidator.TryValidateCombinedAnalysis(
                verdict,
                results,
                sourceNumbers: null,
                out var validationError))
            throw new InvalidOperationException($"多视角分析未通过证据边界校验: {validationError}");
        await publish(AgentStreamEventTypes.DiscussionCompleted, verdict, ct).ConfigureAwait(false);
        return new CombinedAnalysisWorkflowResult { Verdict = verdict, ModelCalls = modelCalls };
    }

    private static async Task<ParticipantResult> RunParticipantAsync(
        IModelClient model,
        CombinedAnalysisTurn turn,
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

    private static PerspectiveAnalysis Normalize(
        PerspectiveAnalysis raw,
        string expectedRole,
        int expectedRound,
        IReadOnlyList<RelatedRecordRef> allowedRelatedRecords,
        IReadOnlyList<PossibleCause> existingPossibleCauses,
        IReadOnlyList<AnalysisToolResult> toolResults)
    {
        var groundedFacts = string.Join('\n', toolResults.Select(static item => $"{item.Summary}\n{item.Data.GetRawText()}"));
        var groundedNumbers = NumberGrounding.ExtractNormalized(groundedFacts);
        var allowed = allowedRelatedRecords.ToDictionary(static item => (item.Kind, item.Id));
        var knownCauseIds = existingPossibleCauses.Select(static item => item.CauseId)
            .ToHashSet(StringComparer.Ordinal);
        var hypotheses = raw.PossibleCauses
            .Where(item => IsSafeStatement(item.Statement) && IsSafeStatement(item.Reason) &&
                           HasGroundedNumbers(item.Statement, groundedNumbers) &&
                           HasGroundedNumbers(item.Reason, groundedNumbers))
            .Take(3)
            .Select((item, index) => item with
            {
                CauseId = string.IsNullOrWhiteSpace(item.CauseId)
                    ? $"h-{expectedRound}-{expectedRole}-{index + 1}"
                    : item.CauseId.Trim()[..Math.Min(item.CauseId.Trim().Length, 100)],
                AuthorRole = expectedRole,
                Statement = Limit(item.Statement, 500),
                Reason = Limit(item.Reason, 1000),
                RelatedRecords = CanonicalRelatedRecords(item.RelatedRecords, allowed)
            })
            .Where(item => item.RelatedRecords.Count > 0 && !knownCauseIds.Contains(item.CauseId))
            .ToArray();
        foreach (var hypothesis in hypotheses)
            knownCauseIds.Add(hypothesis.CauseId);

        var claims = raw.Reviews
            .Where(item => knownCauseIds.Contains(item.CauseId) && IsSafeStatement(item.Statement) &&
                           HasGroundedNumbers(item.Statement, groundedNumbers))
            .Where(item => item.Position is FindingReviewPositions.Support or
                FindingReviewPositions.Oppose or FindingReviewPositions.Uncertain)
            .Take(10)
            .Select(item => item with
            {
                AuthorRole = expectedRole,
                Statement = Limit(item.Statement, 700),
                RelatedRecords = CanonicalRelatedRecords(item.RelatedRecords, allowed)
            })
            .Where(static item => item.RelatedRecords.Count > 0)
            .ToArray();

        return new PerspectiveAnalysis
        {
            Role = expectedRole,
            Round = expectedRound,
            Summary = IsSafeStatement(raw.Summary) && HasGroundedNumbers(raw.Summary, groundedNumbers)
                ? Limit(raw.Summary, 500)
                : "该角色返回的摘要未通过证据边界校验。",
            PossibleCauses = hypotheses,
            Reviews = claims
        };
    }

    private static IReadOnlyList<RelatedRecordRef> CanonicalRelatedRecords(
        IEnumerable<RelatedRecordRef> requested,
        IReadOnlyDictionary<(string Kind, string Id), RelatedRecordRef> allowed)
        => requested.Select(item => allowed.GetValueOrDefault((item.Kind, item.Id)))
            .Where(static item => item is not null)
            .Cast<RelatedRecordRef>()
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();

    private static bool IsSafeStatement(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !AnalysisTextPolicy.ContainsUnsupportedCausalClaim(value);

    private static bool HasGroundedNumbers(string? value, IReadOnlySet<string> groundedNumbers)
        => NumberGrounding.IsGrounded(value, groundedNumbers, out _);

    private static string Limit(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private sealed record ParticipantResult(
        string Role,
        ModelCallResult<PerspectiveAnalysis>? Result,
        string? ErrorType);
}
