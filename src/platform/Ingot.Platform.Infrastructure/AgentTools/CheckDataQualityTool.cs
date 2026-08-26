// 从站点受限事件和时序数据生成只读数据质量证据。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class CheckDataQualityTool(
    IChatEventReader events,
    ITimeSeriesStore timeSeries,
    ProcessExecutionAnalysisEngine? wholeProcessExecutionAnalysis = null,
    IOptions<ChatOptions>? options = null) : IAnalysisTool
{
    private readonly ProcessExecutionAnalysisEngine _wholeProcessExecutionAnalysis = wholeProcessExecutionAnalysis ?? new();
    private readonly ITimeSeriesStore _timeSeries = timeSeries;
    private readonly int _maxEventRows = Math.Clamp(
        options?.Value.MaxEventRowsPerTool ?? 50_000, 1, 1_000_000);
    private readonly int _maxProcessExecutions = Math.Clamp(
        options?.Value.MaxProcessExecutionsPerTool ?? 200, 1, 2_000);
    private readonly int _maxTimeSeriesFrames = Math.Clamp(
        options?.Value.MaxTimeSeriesFramesPerTool ?? 100_000, 1, 1_000_000);

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "check_data_quality",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "检查生产过程执行是否完整、生产信息是否缺失、现场采集是否中断。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "siteId" },
            properties = new
            {
                siteId = new { type = "string", minLength = 1, maxLength = 128 },
                subjectId = new { type = "string" },
                executionId = new { type = "string" }
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
        call.Arguments.TryGetValue("subjectId", out var subjectId);
        call.Arguments.TryGetValue("executionId", out var executionId);
        var scope = new PlatformEventQuery
        {
            SiteId = siteId,
            SubjectId = NullIfBlank(subjectId),
            ExecutionId = NullIfBlank(executionId)
        };

        var stats = await events.GetScopeStatsAsync(context.UserId, scope, ct).ConfigureAwait(false);

        var rows = await events.QueryAllAsync(
            context.UserId,
            scope,
            ct,
            _maxEventRows).ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.IngestId).ToArray();
        var emptyContext = ordered.Count(static row => row.Event.Context.Count == 0);
        var correlations = ordered
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId))
            .GroupBy(static row => row.Event.ExecutionId!, StringComparer.Ordinal)
            .ToArray();
        if (correlations.Length > _maxProcessExecutions)
            throw new InvalidOperationException(
                $"数据质量检查涉及 {correlations.Length} 个过程执行，超过 {_maxProcessExecutions} 个预算；请缩小查询范围。");
        var incompleteProcessExecutions = correlations.Count(group =>
            group.Any(static row => row.Event.EventType.EndsWith(".started", StringComparison.Ordinal)) !=
            group.Any(static row =>
                row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
                row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
                row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal)));
        var processQuality = new List<ProcessDataQualitySummary>();
        var remainingFrames = _maxTimeSeriesFrames;
        foreach (var group in correlations)
        {
            if (remainingFrames == 0)
                throw new TimeSeriesQueryLimitExceededException(_maxTimeSeriesFrames);
            var startedAt = group.FirstOrDefault(static row =>
                row.Event.EventType == "process.execution.started")?.Event.OccurredAt;
            var completedAt = group.LastOrDefault(static row =>
                row.Event.EventType == "process.execution.completed")?.Event.OccurredAt;
            var samples = await TimeSeriesFrameReader.QueryAllAsync(
                _timeSeries,
                new TimeSeriesQuery { SiteId = siteId, ExecutionId = group.Key },
                ct,
                remainingFrames).ConfigureAwait(false);
            remainingFrames -= samples.Count;
            processQuality.Add(_wholeProcessExecutionAnalysis.Analyze(
                samples, startedAt, completedAt, null, null).Quality);
        }
        var degradedProcessProcessExecutions = processQuality.Count(static quality =>
            quality.Status == ProcessDataStatuses.Degraded);
        var unavailableProcessProcessExecutions = processQuality.Count(static quality =>
            quality.Status == ProcessDataStatuses.Unavailable);
        var scopedQuery = !string.IsNullOrWhiteSpace(subjectId) || !string.IsNullOrWhiteSpace(executionId);
        int? sequenceGaps = null;
        if (!scopedQuery)
        {
            sequenceGaps = ordered
                .GroupBy(static row => row.EdgeId, StringComparer.OrdinalIgnoreCase)
                .Sum(static group =>
                {
                    var sequences = group.Select(static row => row.Event.Seq).Distinct().Order().ToArray();
                    return sequences.Zip(sequences.Skip(1)).Count(static pair => pair.Second > pair.First + 1);
                });
        }

        var latest = stats.LatestOccurredAt;
        var totalEvents = stats.Count;
        var scopeEmpty = totalEvents == 0;
        var limitations = new List<string>();
        if (scopeEmpty)
            limitations.Add("当前范围没有生产记录，无法检查过程执行是否完整或采集是否中断。");
        else if (scopedQuery)
            limitations.Add("按对象或过程执行过滤后的事件不是完整 Edge 序列，因此不计算序号连续性。");
        if (degradedProcessProcessExecutions > 0)
            limitations.Add($"有 {degradedProcessProcessExecutions} 个过程执行存在采样空窗、重复时间戳或源序号间断，比较时需要降级处理。");
        if (unavailableProcessProcessExecutions > 0)
            limitations.Add($"有 {unavailableProcessProcessExecutions} 个过程执行没有可用的过程数据。");
        var scopeId = $"{siteId}:events:{subjectId ?? "*"}:{executionId ?? "*"}:{ordered.FirstOrDefault()?.IngestId ?? 0}-{ordered.LastOrDefault()?.IngestId ?? 0}";
        var relatedRecords = new RelatedRecordRef
        {
            Kind = "event-query",
            Id = scopeId,
            Label = $"生产事件查询结果（已完整检查 {ordered.Length} 条）",
            Url = BuildEventsUrl(siteId, subjectId, executionId)
        };
        var summary = scopeEmpty
            ? "当前范围没有生产事件，无法检查数据完整性。"
            : $"范围内共 {totalEvents} 条生产事件，已完整检查 {ordered.Length} 条：涉及 {correlations.Length} 个生产运行，发现 {incompleteProcessExecutions} 个不完整运行、" +
              (sequenceGaps.HasValue ? $"{sequenceGaps} 个序号间断，" : "序号连续性未在当前过滤范围计算，") +
              $"{emptyContext} 条记录缺少生产信息；最新记录时间为 {latest:O}。";
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                eventCount = ordered.Length,
                totalEventCount = totalEvents,
                correlationCount = correlations.Length,
                incompleteProcessExecutions,
                degradedProcessProcessExecutions,
                unavailableProcessProcessExecutions,
                sequenceGaps,
                emptyContext,
                latestOccurredAt = latest,
                earliestOccurredAt = stats.EarliestOccurredAt
            }),
            RelatedRecords = [relatedRecords],
            Limitations = limitations,
            Outcome = scopeEmpty || incompleteProcessExecutions > 0 || unavailableProcessProcessExecutions > 0 ||
                      sequenceGaps is > 0
                ? AnalysisToolOutcomes.InsufficientData
                : AnalysisToolOutcomes.Sufficient
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildEventsUrl(string siteId, string? subjectId, string? executionId)
    {
        var values = new List<string> { $"siteId={Uri.EscapeDataString(siteId)}" };
        if (!string.IsNullOrWhiteSpace(subjectId))
            values.Add($"subjectId={Uri.EscapeDataString(subjectId)}");
        if (!string.IsNullOrWhiteSpace(executionId))
            values.Add($"executionId={Uri.EscapeDataString(executionId)}");
        return $"/events?{string.Join('&', values)}";
    }

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));

}
