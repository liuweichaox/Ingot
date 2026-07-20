using System.Text.Json;
using Ingot.Contracts.Agents;

namespace Ingot.Agent;

/// <summary>
///     无外部模型时使用的安全基线模型。它只把明确的问题映射到白名单工具，
///     用于本地部署、回归测试和模型故障降级。
/// </summary>
public sealed class DeterministicModelClient : IModelClient
{
    public string Provider => "Deterministic";

    public string Model => "deterministic-v1";

    public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
        CreateChatRunRequest request,
        IReadOnlyCollection<AnalysisToolDefinition> tools,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var available = tools.Select(static tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var calls = new List<AnalysisToolCall>();
        var question = request.Question;
        var context = request.PageContext;

        if (ContainsAny(question, "质量", "完整", "缺失", "缺口", "断网", "健康") &&
            available.Contains("check_data_quality"))
        {
            calls.Add(new AnalysisToolCall
            {
                Tool = "check_data_quality",
                Arguments = ContextArguments(context)
            });
        }

        if (context is not null &&
            context.Kind is "cycle" or "correlation" or "operation-run" &&
            available.Contains("get_cycle_trace"))
        {
            calls.Add(new AnalysisToolCall
            {
                Tool = "get_cycle_trace",
                Arguments = new Dictionary<string, string?>
                {
                    ["correlationId"] = context.Id
                }
            });
        }

        if (context is not null &&
            context.Kind is "cycle" or "correlation" or "operation-run" &&
            ContainsAny(question, "比较", "不同", "差异", "上批", "同类", "趋势", "recent") &&
            available.Contains("find_comparable_cycles"))
        {
            calls.Add(new AnalysisToolCall
            {
                Tool = "find_comparable_cycles",
                Arguments = new Dictionary<string, string?>
                {
                    ["correlationId"] = context.Id,
                    ["limit"] = "20"
                }
            });
        }

        if (calls.Count == 0 && available.Contains("check_data_quality"))
        {
            calls.Add(new AnalysisToolCall
            {
                Tool = "check_data_quality",
                Arguments = ContextArguments(context)
            });
        }

        return Task.FromResult(Result(new AnalysisPlan
        {
            Intent = calls.Count == 1 ? calls[0].Tool : "combined_governed_work",
            Summary = $"使用 {calls.Count} 个已授权工具完成任务。",
            ToolCalls = calls
        }, "intent.resolve"));
    }

    public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var relatedRecords = results.SelectMany(static result => result.RelatedRecords)
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        var limitations = results.SelectMany(static result => result.Limitations)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var findings = results.Select(static result => result.Summary).ToArray();
        var followUps = new[]
        {
            "是否需要指定具体设备或生产周期？",
            "是否需要查看该生产周期的完整记录？"
        };
        return Task.FromResult(Result(new AnalysisAnswer
        {
            Summary = findings.Length == 0
                ? "没有足够的生产数据回答该问题。"
                : string.Join(" ", findings),
            Findings = findings,
            Limitations = limitations,
            RelatedRecords = relatedRecords,
            FollowUpQuestions = followUps
        }, "answer.compose"));
    }

    public Task<ModelCallResult<PerspectiveAnalysis>> ParticipateAsync(
        CombinedAnalysisTurn turn,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var relatedRecords = turn.ToolResults.SelectMany(static result => result.RelatedRecords)
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        if (turn.Round == 1)
        {
            var (statement, reason) = turn.Role switch
            {
                AnalysisPerspectives.Process => (
                    "需要检查工艺状态或周期参数变化是否与当前现象同步。",
                    "当前查询结果包含生产周期过程和数据完整性信息。"),
                AnalysisPerspectives.Quality => (
                    "需要检查检测结果与周期特征之间是否存在稳定关联。",
                    "现有记录可以界定周期范围，但仍需要质量样本和特征统计才能评价关联强度。"),
                _ => (
                    "当前现象可能受到数据缺失、样本范围或关联信息不完整的影响。",
                    "在提出原因解释前，应先排除周期未配对、序号间断和生产信息缺失。")
            };
            return Task.FromResult(Result(new PerspectiveAnalysis
            {
                Role = turn.Role,
                Round = turn.Round,
                Summary = "提出一个需要更多生产数据或统计分析确认的可能原因。",
                PossibleCauses = relatedRecords.Length == 0
                    ? []
                    :
                    [
                        new PossibleCause
                        {
                            CauseId = $"h-{turn.Role}",
                            AuthorRole = turn.Role,
                            Statement = statement,
                            Reason = reason,
                            RelatedRecords = relatedRecords
                        }
                    ]
            }, "combinedAnalysis.review"));
        }

        var claims = turn.PossibleCauses.Select(hypothesis => new FindingReview
        {
            CauseId = hypothesis.CauseId,
            AuthorRole = turn.Role,
            Position = turn.Role == AnalysisPerspectives.Review
                ? FindingReviewPositions.Oppose
                : FindingReviewPositions.Uncertain,
            Statement = turn.Role == AnalysisPerspectives.Review
                ? "现有生产记录只能支持继续分析，不能确认该项就是原因。"
                : "当前数据与该项可能原因相关，但还缺少同类周期比较或质量关系分析。",
            RelatedRecords = relatedRecords
        }).ToArray();
        return Task.FromResult(Result(new PerspectiveAnalysis
        {
            Role = turn.Role,
            Round = turn.Round,
            Summary = "复核可能原因并说明当前数据能支持到什么程度。",
            Reviews = claims
        }, "combinedAnalysis.review"));
    }

    private ModelCallResult<T> Result<T>(T value, string operation)
        => new()
        {
            Value = value,
            Usage = new ModelCallUsage
            {
                Provider = Provider,
                Model = Model,
                Operation = operation
            }
        };

    private static Dictionary<string, string?> ContextArguments(PageContextRef? context)
    {
        var arguments = new Dictionary<string, string?>();
        if (context is null)
            return arguments;
        if (context.Kind is "asset" or "equipment" or "source")
            arguments["subjectId"] = context.Id;
        else if (context.Kind is "cycle" or "correlation" or "operation-run")
            arguments["correlationId"] = context.Id;
        return arguments;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public sealed class DefaultModelRouter(IEnumerable<IModelClient> clients) : IModelRouter
{
    private readonly IReadOnlyList<IModelClient> _clients = clients.ToArray();

    public IModelClient GetClient(string entryPoint, ModelRole role)
        => _clients.FirstOrDefault(client => string.Equals(client.EntryPoint, entryPoint, StringComparison.Ordinal))
           ?? _clients.FirstOrDefault(client => client.EntryPoint == "*")
           ?? throw new InvalidOperationException($"功能入口 {entryPoint} 没有可用分析模型。");
}
