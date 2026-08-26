// 在单一授权站点内寻找上下文匹配的可比较过程执行。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class FindComparableExecutionsTool(
    IChatEventReader events,
    ProcessAnalysisResolver? analysisResolver = null) : IAnalysisTool
{
    private static readonly string[] ComparableKeys =
    [
        "product_code",
        "operation_code",
        "process_specification_id",
        "process_specification_version",
        "process_template",
        "tooling_assembly_id",
        "cavity_id",
        "preform_lot"
    ];

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "find_comparable_executions",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按产品、工序和工艺规范查找同类生产过程执行。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "siteId", "executionId" },
            properties = new
            {
                siteId = new { type = "string", minLength = 1, maxLength = 128 },
                executionId = new { type = "string", minLength = 1, maxLength = 200 },
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
        var siteId = context.AccessScope.EnsureAuthorizedSite(Require(call, "siteId"));
        var executionId = Require(call, "executionId").Trim();
        var limit = ParseLimit(call.Arguments.GetValueOrDefault("limit"), 20, 1, 200);
        var currentRows = await events.QueryAllAsync(
            context.UserId,
            new PlatformEventQuery { SiteId = siteId, ExecutionId = executionId },
            ct).ConfigureAwait(false);
        if (currentRows.Count == 0)
            return Empty(siteId, executionId);

        var currentContext = currentRows.Select(static row => row.Event.Context)
            .FirstOrDefault(static item => item.Count > 0) ?? new Dictionary<string, string>();
        var analysis = analysisResolver is null
            ? null
            : await analysisResolver.ResolveAsync(currentContext, "production-execution", ct).ConfigureAwait(false);
        var configuredKeys = analysis?.Plan.ComparisonKeys.Count > 0
            ? analysis.Plan.ComparisonKeys
            : analysisResolver is null ? ComparableKeys : [];
        var contextFacts = configuredKeys
            .Select(key => new
            {
                Key = key.Replace('.', '_'),
                Value = ProcessAnalysisResolver.ContextValue(currentContext, key)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(static item => item.Key, static item => item.Value!, StringComparer.Ordinal);
        if (contextFacts.Count == 0)
        {
            return new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary = $"过程执行 {executionId} 缺少可用于同类检索的保留生产信息项。",
                Data = JsonSerializer.SerializeToElement(new { executionId, comparableProcessExecutions = Array.Empty<object>() }),
                RelatedRecords =
                [
                    new RelatedRecordRef
                    {
                        Kind = "event-query",
                        Id = $"{siteId}:correlation:{executionId}",
                        Label = $"过程执行 {executionId} 事件",
                        Url = $"/api/v1/events?siteId={Uri.EscapeDataString(siteId)}&executionId={Uri.EscapeDataString(executionId)}&limit=500"
                    }
                ],
                Limitations = [analysis is null
                    ? "当前过程执行没有匹配的已发布分析方案。"
                    : "当前过程执行缺少分析方案要求的同类比较键。"],
                Outcome = AnalysisToolOutcomes.InsufficientData
            };
        }

        var queryContext = analysisResolver is null
            ? contextFacts.Where(static pair => pair.Key is "product_code" or "operation_code" or "process_specification_id" or "process_specification_version")
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
            : contextFacts;
        if (queryContext.Count == 0)
            queryContext = contextFacts;
        var candidates = await events.QueryAllAsync(
            context.UserId,
            new PlatformEventQuery { SiteId = siteId, Context = queryContext },
            ct).ConfigureAwait(false);

        var comparable = candidates
            .Where(row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId) &&
                          !string.Equals(row.Event.ExecutionId, executionId, StringComparison.Ordinal))
            .GroupBy(row => row.Event.ExecutionId!, StringComparer.Ordinal)
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
                return new ComparableProcessExecution(group.Key, startedAt, keys.Length, keys);
            })
            .Where(static item => item.MatchedKeyCount > 0)
            .OrderByDescending(static item => item.MatchedKeyCount)
            .ThenByDescending(static item => item.StartedAt)
            .Take(limit)
            .ToArray();
        var comparableData = comparable.Select(static item => new
        {
            item.ExecutionId,
            item.StartedAt,
            item.MatchedKeyCount,
            item.MatchedKeys
        }).Select(static item => new
        {
            executionId = item.ExecutionId,
            startedAt = item.StartedAt,
            matchedKeyCount = item.MatchedKeyCount,
            matchedKeys = item.MatchedKeys
        }).ToArray();

        var limitations = new List<string>();
        if (comparable.Length == 0)
            limitations.Add("没有找到共享保留生产信息项的其他过程执行。");

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"过程执行 {executionId} 找到 {comparable.Length} 个可对比过程执行，对比条件：{string.Join("、", queryContext.Keys)}。",
            Data = JsonSerializer.SerializeToElement(new
            {
                executionId,
                criteria = queryContext,
                comparableProcessExecutions = comparableData
            }),
            Details =
            [
                new ResultDetailLink
                {
                    Kind = "event-query",
                    Label = "同类过程执行生产记录明细（分页）",
                    Url = BuildEventsUrl(siteId, queryContext)
                }
            ],
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "event-query",
                    Id = $"{siteId}:comparable:{executionId}",
                    Label = $"过程执行 {executionId} 同类检索",
                    Url = BuildEventsUrl(siteId, queryContext)
                }
            ],
            Limitations = limitations,
            Outcome = comparable.Length == 0 ? AnalysisToolOutcomes.InsufficientData : AnalysisToolOutcomes.Sufficient
        };
    }

    private static AnalysisToolResult Empty(string siteId, string executionId)
        => new()
        {
            Tool = "find_comparable_executions",
            Summary = $"没有找到过程执行 {executionId}。",
            Data = JsonSerializer.SerializeToElement(new { executionId, comparableProcessExecutions = Array.Empty<object>() }),
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "event-query",
                    Id = $"{siteId}:correlation:{executionId}",
                    Label = $"过程执行 {executionId} 事件",
                    Url = $"/api/v1/events?siteId={Uri.EscapeDataString(siteId)}&executionId={Uri.EscapeDataString(executionId)}&limit=500"
                }
            ],
            Limitations = ["当前生产过程执行号没有对应的生产记录。"],
            Outcome = AnalysisToolOutcomes.InsufficientData
        };

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));

    private static int ParseLimit(string? value, int fallback, int min, int max)
        => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;

    private static string BuildEventsUrl(string siteId, IReadOnlyDictionary<string, string> context)
    {
        var query = new List<string>
        {
            $"siteId={Uri.EscapeDataString(siteId)}",
            "limit=500"
        };
        query.AddRange(context.Select(pair =>
            $"ctx.{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"/api/v1/events?{string.Join('&', query)}";
    }

    private sealed record ComparableProcessExecution(
        string ExecutionId,
        DateTimeOffset StartedAt,
        int MatchedKeyCount,
        IReadOnlyList<string> MatchedKeys);
}
