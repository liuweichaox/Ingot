// 验证组合分析的引用、哈希、预算与不支持结论拒绝规则。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class CombinedAnalysisValidationTests
{
    [Fact]
    public void FinalAnswerRejectsCausalLanguageInsideCombinedAnalysis()
    {
        var answer = Answer(Combined("温度导致缺陷。", "现有记录需要复核。"));

        var accepted = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            answer, [ToolResult()], out var error);

        Assert.False(accepted);
        Assert.Contains("因果", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalAnswerRejectsUnsupportedNumberInsideCombinedAnalysis()
    {
        var answer = Answer(Combined("现有记录显示可能关联。", "新指标为 73。"));

        var accepted = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            answer, [ToolResult()], out var error);

        Assert.False(accepted);
        Assert.Contains("73", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowNeverStreamsRejectedParticipantLanguage()
    {
        var options = Options.Create(new ChatOptions
        {
            MaxDiscussionRounds = 1,
            MaxDiscussionTurns = 3
        });
        var workflow = new BoundedCombinedAnalysisWorkflow(options);
        var published = new List<(string Type, object? Data)>();

        await workflow.RunAsync(
            new CreateChatRunRequest { Question = "检查缺陷关联", Mode = "combined" },
            new AnalysisPlan { Intent = "compare", Summary = "只读比较" },
            [ToolResult()],
            new UnsafeParticipantModel(),
            (type, data, _) =>
            {
                published.Add((type, data));
                return Task.CompletedTask;
            });

        var messages = published
            .Where(static item => item.Type == AgentStreamEventTypes.DiscussionMessage)
            .Select(static item => Assert.IsType<PerspectiveAnalysis>(item.Data))
            .ToArray();
        var completed = Assert.IsType<CombinedAnalysisResult>(published.Single(static item =>
            item.Type == AgentStreamEventTypes.DiscussionCompleted).Data);
        var exposedText = messages.Select(static item => item.Summary)
            .Concat(messages.SelectMany(static item => item.PossibleCauses.SelectMany(static cause =>
                new[] { cause.Statement, cause.Reason })))
            .Concat(AnalysisTextPolicy.EnumerateCombinedAnalysisText(completed))
            .ToArray();

        Assert.NotEmpty(messages);
        Assert.DoesNotContain(exposedText, static value => value.Contains("温度导致缺陷", StringComparison.Ordinal));
        Assert.DoesNotContain(exposedText, AnalysisTextPolicy.ContainsUnsupportedCausalClaim);
        Assert.All(messages, static item =>
            Assert.Contains("未通过证据边界校验", item.Summary, StringComparison.Ordinal));
    }

    private static AnalysisAnswer Answer(CombinedAnalysisResult combined) => new()
    {
        Summary = "现有记录只支持待复核结论。",
        RelatedRecords = [Reference()],
        CombinedAnalysis = combined
    };

    private static CombinedAnalysisResult Combined(string statement, string reason) => new()
    {
        Status = "needs-review",
        Summary = "现有记录只支持待复核结论。",
        PossibleCauses =
        [
            new PossibleCause
            {
                CauseId = "h-temperature",
                AuthorRole = AnalysisPerspectives.Process,
                Statement = statement,
                Reason = reason,
                RelatedRecords = [Reference()]
            }
        ],
        RelatedRecords = [Reference()]
    };

    private static AnalysisToolResult ToolResult() => new()
    {
        Tool = "check_data_quality",
        Summary = "完整率为 0.95。",
        Data = JsonSerializer.SerializeToElement(new { completeness = 0.95 }),
        RelatedRecords = [Reference()]
    };

    private static RelatedRecordRef Reference() => new()
    {
        Kind = "event-query",
        Id = "SITE-001:events:1",
        Label = "站点事件查询"
    };

    private sealed class UnsafeParticipantModel : IModelClient
    {
        public string EntryPoint => ProductEntryPoints.Chat;
        public string Provider => "test";
        public string Model => "test";
        public string ModelFor(ModelRole role) => Model;

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateChatRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateChatRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<PerspectiveAnalysis>> ParticipateAsync(
            CombinedAnalysisTurn turn,
            CancellationToken ct = default)
            => Task.FromResult(new ModelCallResult<PerspectiveAnalysis>
            {
                Value = new PerspectiveAnalysis
                {
                    Role = turn.Role,
                    Round = turn.Round,
                    Summary = "温度导致缺陷。",
                    PossibleCauses =
                    [
                        new PossibleCause
                        {
                            CauseId = $"h-{turn.Role}",
                            AuthorRole = turn.Role,
                            Statement = "温度导致缺陷。",
                            Reason = "温度直接导致质量异常。",
                            RelatedRecords = [Reference()]
                        }
                    ]
                },
                Usage = new ModelCallUsage
                {
                    Provider = Provider,
                    Model = Model,
                    Operation = "combined-participant"
                }
            });
    }
}
