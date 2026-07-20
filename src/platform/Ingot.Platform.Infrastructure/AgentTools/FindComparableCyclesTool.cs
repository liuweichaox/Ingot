using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class FindComparableCyclesTool(IChatEventReader events) : IAnalysisTool
{
    private static readonly string[] ComparableKeys =
    [
        "product_code",
        "operation_code",
        "recipe_id",
        "recipe_version",
        "recipe_template",
        "mold_id",
        "cavity_id",
        "preform_lot"
    ];

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "find_comparable_cycles",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按产品、工序和配方查找同类生产周期。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "correlationId" },
            properties = new
            {
                correlationId = new { type = "string", minLength = 1, maxLength = 200 },
                limit = new { type = "string", minLength = 1, maxLength = 3 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var correlationId = Require(call, "correlationId").Trim();
        var limit = ParseLimit(call.Arguments.GetValueOrDefault("limit"), 20, 1, 200);
        var currentRows = await events.QueryAsync(
            context.UserId,
            new PlatformEventQuery { CorrelationId = correlationId, Limit = 500 },
            ct).ConfigureAwait(false);
        if (currentRows.Count == 0)
            return Empty(correlationId);

        var contextFacts = currentRows
            .SelectMany(static row => row.Event.Context)
            .Where(static pair => ComparableKeys.Contains(pair.Key, StringComparer.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static pair => pair.Value).First(),
                StringComparer.Ordinal);
        if (contextFacts.Count == 0)
        {
            return new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary = $"周期 {correlationId} 缺少可用于同类检索的保留生产信息项。",
                Data = JsonSerializer.SerializeToElement(new { correlationId, comparableCycles = Array.Empty<object>() }),
                RelatedRecords =
                [
                    new RelatedRecordRef
                    {
                        Kind = "event-query",
                        Id = $"correlation:{correlationId}",
                        Label = $"周期 {correlationId} 事件",
                        Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(correlationId)}&limit=500"
                    }
                ],
                Limitations = ["当前周期缺少 product_code、operation_code、recipe_id 等同类检索键。"],
                Outcome = AnalysisToolOutcomes.InsufficientData
            };
        }

        var strictKeys = contextFacts.Keys
            .Where(static key => key is "product_code" or "operation_code" or "recipe_id" or "recipe_version")
            .ToDictionary(key => key, key => contextFacts[key], StringComparer.Ordinal);
        var queryContext = strictKeys.Count == 0 ? contextFacts.Take(2).ToDictionary() : strictKeys;
        var candidates = await events.QueryAsync(
            context.UserId,
            new PlatformEventQuery { Context = queryContext, Limit = 500 },
            ct).ConfigureAwait(false);

        var comparable = candidates
            .Where(row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId) &&
                          !string.Equals(row.Event.CorrelationId, correlationId, StringComparison.Ordinal))
            .GroupBy(row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var keys = group.SelectMany(row => row.Event.Context)
                    .Where(pair => contextFacts.TryGetValue(pair.Key, out var expected) &&
                                   string.Equals(expected, pair.Value, StringComparison.Ordinal))
                    .Select(static pair => pair.Key)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var startedAt = group.Min(static row => row.Event.OccurredAt);
                return new ComparableCycle(group.Key, startedAt, keys.Length, keys);
            })
            .Where(static item => item.MatchedKeyCount > 0)
            .OrderByDescending(static item => item.MatchedKeyCount)
            .ThenByDescending(static item => item.StartedAt)
            .Take(limit)
            .ToArray();
        var comparableData = comparable.Select(static item => new
        {
            item.CorrelationId,
            item.StartedAt,
            item.MatchedKeyCount,
            item.MatchedKeys
        }).Select(static item => new
        {
            correlationId = item.CorrelationId,
            startedAt = item.StartedAt,
            matchedKeyCount = item.MatchedKeyCount,
            matchedKeys = item.MatchedKeys
        }).ToArray();

        var limitations = new List<string>();
        if (candidates.Count == 500)
            limitations.Add("同类周期查询超过 500 条生产记录，可能还有周期没有显示。");
        if (comparable.Length == 0)
            limitations.Add("没有找到共享保留生产信息项的其他周期。");

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"周期 {correlationId} 找到 {comparable.Length} 个可对比周期，对比条件：{string.Join("、", queryContext.Keys)}。",
            Data = JsonSerializer.SerializeToElement(new
            {
                correlationId,
                criteria = queryContext,
                comparableCycles = comparableData
            }),
            Details =
            [
                new ResultDetailLink
                {
                    Kind = "event-query",
                    Label = "完整同类周期生产记录",
                    Url = BuildEventsUrl(queryContext)
                }
            ],
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "event-query",
                    Id = $"comparable:{correlationId}",
                    Label = $"周期 {correlationId} 同类检索",
                    Url = BuildEventsUrl(queryContext)
                }
            ],
            Limitations = limitations,
            Outcome = comparable.Length == 0 ? AnalysisToolOutcomes.InsufficientData : AnalysisToolOutcomes.Sufficient
        };
    }

    private static AnalysisToolResult Empty(string correlationId)
        => new()
        {
            Tool = "find_comparable_cycles",
            Summary = $"没有找到周期 {correlationId}。",
            Data = JsonSerializer.SerializeToElement(new { correlationId, comparableCycles = Array.Empty<object>() }),
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "event-query",
                    Id = $"correlation:{correlationId}",
                    Label = $"周期 {correlationId} 事件",
                    Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(correlationId)}&limit=500"
                }
            ],
            Limitations = ["当前生产周期号没有对应的生产记录。"],
            Outcome = AnalysisToolOutcomes.InsufficientData
        };

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));

    private static int ParseLimit(string? value, int fallback, int min, int max)
        => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;

    private static string BuildEventsUrl(IReadOnlyDictionary<string, string> context)
    {
        var query = new List<string> { "limit=500" };
        query.AddRange(context.Select(pair =>
            $"ctx.{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"/api/v1/events?{string.Join('&', query)}";
    }

    private sealed record ComparableCycle(
        string CorrelationId,
        DateTimeOffset StartedAt,
        int MatchedKeyCount,
        IReadOnlyList<string> MatchedKeys);
}
