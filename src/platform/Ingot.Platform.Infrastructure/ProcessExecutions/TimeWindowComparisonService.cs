// 在站点授权范围内组装时间窗口运行、检验和过程特征比较。
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.TimeSeries;
using Ingot.Platform.Infrastructure.Inspections;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class TimeWindowComparisonService(
    IPlatformEventStore events,
    ProcessAnalysisResolver analysisResolver,
    IInspectionRecordStore inspections,
    ITimeSeriesStore timeSeries,
    ProcessExecutionAnalysisEngine? wholeProcessExecutionAnalysis = null) : ITimeWindowComparisonService
{
    private const int MaximumTotalEventRows = 100_000;
    private const int MaximumTotalTimeSeriesFrames = 200_000;
    private readonly ProcessExecutionAnalysisEngine _wholeProcessExecutionAnalysis = wholeProcessExecutionAnalysis ?? new();
    private readonly ITimeSeriesStore _timeSeries = timeSeries;

    public async Task<TimeWindowComparisonResult> CompareAsync(
        TimeWindowComparisonRequest request,
        string siteId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("连续过程比较必须指定站点。", nameof(siteId));
        siteId = siteId.Trim();
        var scope = request.AnalysisScope?.Trim().ToLowerInvariant();
        if (scope is not ("analysis-window" or "production-run"))
            throw new ArgumentException("连续过程比较只支持 analysis-window 或 production-run。", nameof(request));
        if (request.Windows.Count < 2 || request.Windows.Count > 20)
            throw new ArgumentException("请选择 2 到 20 个运行段或分析窗口。", nameof(request));
        var ids = request.Windows.Select(static item => item.WindowId?.Trim()).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            !ids.Contains(request.BaselineWindowId?.Trim(), StringComparer.Ordinal))
            throw new ArgumentException("窗口编号必须唯一，且必须从中指定基准窗口。", nameof(request));
        if (request.Windows.Any(static item => item.From == default || item.To == default || item.From >= item.To ||
                                               string.IsNullOrWhiteSpace(item.SubjectType) || string.IsNullOrWhiteSpace(item.SubjectId)))
            throw new ArgumentException("每个窗口必须包含有效的对象和起止时间。", nameof(request));

        var rows = new Dictionary<string, IReadOnlyList<PlatformProductionEvent>>(StringComparer.Ordinal);
        var samples = new Dictionary<string, IReadOnlyList<ProcessSampleFrame>>(StringComparer.Ordinal);
        var remainingEventRows = MaximumTotalEventRows;
        var remainingFrames = MaximumTotalTimeSeriesFrames;
        foreach (var window in request.Windows)
        {
            if (remainingEventRows == 0)
                throw new PlatformEventQueryLimitExceededException(MaximumTotalEventRows);
            rows[window.WindowId] = await QueryAllAsync(new PlatformEventQuery
            {
                SiteId = siteId,
                SubjectType = window.SubjectType.Trim(),
                SubjectId = window.SubjectId.Trim(),
                From = window.From.ToUniversalTime(),
                To = window.To.ToUniversalTime()
            }, remainingEventRows, ct).ConfigureAwait(false);
            remainingEventRows -= rows[window.WindowId].Count;
            if (remainingFrames == 0)
                throw new TimeSeriesQueryLimitExceededException(MaximumTotalTimeSeriesFrames);
            samples[window.WindowId] = await TimeSeriesFrameReader.QueryAllAsync(_timeSeries, new TimeSeriesQuery
            {
                SiteId = siteId,
                From = window.From.ToUniversalTime(),
                To = window.To.ToUniversalTime(),
                SubjectType = window.SubjectType.Trim(),
                SubjectId = window.SubjectId.Trim(),
            }, ct, remainingFrames).ConfigureAwait(false);
            remainingFrames -= samples[window.WindowId].Count;
        }
        var baselineSelection = request.Windows.Single(item => item.WindowId == request.BaselineWindowId);
        var baselineRows = rows[baselineSelection.WindowId];
        if (baselineRows.Count == 0)
            throw new ArgumentException("基准窗口内没有生产数据。", nameof(request));
        var baselineContext = ResolveContext(baselineRows);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, scope, ct).ConfigureAwait(false)
                       ?? throw new ArgumentException("基准窗口没有匹配的已发布分析方案。", nameof(request));
        var comparisonKeys = analysis.Plan.ComparisonKeys;
        EnsureComparisonKeysPresent(baselineContext, comparisonKeys, "基准窗口");
        EnsureComparisonKeysConsistent(baselineRows, comparisonKeys, "基准窗口");
        foreach (var window in request.Windows.Where(item => item.WindowId != baselineSelection.WindowId))
        {
            if (rows[window.WindowId].Count == 0)
                throw new ArgumentException($"窗口 {window.WindowId} 内没有生产数据。", nameof(request));
            var comparisonContext = ResolveContext(rows[window.WindowId]);
            EnsureComparisonKeysPresent(comparisonContext, comparisonKeys, $"窗口 {window.WindowId}");
            EnsureComparisonKeysConsistent(rows[window.WindowId], comparisonKeys, $"窗口 {window.WindowId}");
            if (!comparisonKeys.All(key => string.Equals(
                    ProcessAnalysisResolver.ContextValue(baselineContext, key),
                    ProcessAnalysisResolver.ContextValue(comparisonContext, key),
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"窗口 {window.WindowId} 与基准窗口的同类比较键不一致。", nameof(request));
            }
        }

        var scopes = await inspections.ListScopesAsync(siteId, ct).ConfigureAwait(false);
        var scopesByWindow = request.Windows.ToDictionary(
            static window => window.WindowId,
            window => scopes.Where(scope => ScopeBelongsToWindow(scope, window)).ToArray(),
            StringComparer.Ordinal);
        var scopeIds = scopesByWindow.Values
            .SelectMany(static items => items)
            .Select(static scope => scope.ScopeId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var inspectionRecords = scopeIds.Length == 0
            ? []
            : InspectionRecordSet.Effective(
                await inspections.QueryAllByExecutionIdsAsync(scopeIds, siteId, ct).ConfigureAwait(false));
        var resultRows = request.Windows.Select(window => BuildRow(
            window,
            rows[window.WindowId],
            samples[window.WindowId],
            analysis,
            scopesByWindow[window.WindowId],
            inspectionRecords)).ToArray();
        var baseline = resultRows.Single(item => item.WindowId == request.BaselineWindowId);
        return new TimeWindowComparisonResult
        {
            BaselineWindowId = request.BaselineWindowId!,
            AnalysisPlanId = analysis.Plan.PlanId,
            AnalysisPlanVersion = analysis.Plan.Version,
            DataModelId = analysis.DataModel.ModelId,
            DataModelVersion = analysis.DataModel.Version,
            AnalysisScope = analysis.Plan.AnalysisScope,
            AlignmentMode = analysis.Plan.AlignmentMode,
            Baseline = baseline,
            ComparisonWindows = resultRows.Where(item => item.WindowId != request.BaselineWindowId).ToArray()
        };
    }

    private TimeWindowComparisonRow BuildRow(
        TimeWindowSelection window,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<ProcessSampleFrame> samples,
        ResolvedProcessAnalysis analysis,
        IReadOnlyList<InspectionScope> scopes,
        IReadOnlyList<InspectionRecord> inspectionRecords)
    {
        var processAnalysis = _wholeProcessExecutionAnalysis.Analyze(
            samples,
            window.From.ToUniversalTime(),
            window.To.ToUniversalTime(),
            analysis.DataModel,
            analysis.Plan);
        return new TimeWindowComparisonRow
        {
            WindowId = window.WindowId,
            Label = window.Label,
            SubjectType = window.SubjectType,
            SubjectId = window.SubjectId,
            From = window.From.ToUniversalTime(),
            To = window.To.ToUniversalTime(),
            EventCount = rows.Count,
            SampleCount = samples.Count,
            ProcessDataQuality = processAnalysis.Quality,
            Context = ResolveContext(rows),
            Quality = BuildQuality(scopes, inspectionRecords),
            Signals = processAnalysis.Signals
        };
    }

    private static bool ScopeBelongsToWindow(
        InspectionScope scope,
        TimeWindowSelection window)
        => string.Equals(scope.ScopeId, window.WindowId, StringComparison.Ordinal) ||
           (string.Equals(scope.SubjectType, window.SubjectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(scope.SubjectId, window.SubjectId, StringComparison.OrdinalIgnoreCase) &&
            scope.From >= window.From.ToUniversalTime() &&
            scope.To <= window.To.ToUniversalTime());

    private static TimeWindowQualitySummary BuildQuality(
        IReadOnlyList<InspectionScope> scopes,
        IReadOnlyList<InspectionRecord> allRecords)
    {
        var scopeIds = scopes.Select(static scope => scope.ScopeId).ToHashSet(StringComparer.Ordinal);
        var records = allRecords.Where(record => scopeIds.Contains(record.ExecutionId)).ToArray();
        var passCount = records.Count(static record => record.Outcome == "PASS");
        var failCount = records.Count(static record => record.Outcome == "FAIL");
        var values = records
            .SelectMany(static record => record.Measurements)
            .Where(static measurement => measurement.NumericValue.HasValue)
            .GroupBy(static measurement => measurement.CharacteristicCode, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var samples = group.Select(static measurement => (double)measurement.NumericValue!.Value).ToArray();
                return new TimeWindowQualityCharacteristic
                {
                    Code = group.Key,
                    SampleCount = samples.Length,
                    Average = samples.Length == 0 ? null : samples.Average(),
                    Minimum = samples.Length == 0 ? null : samples.Min(),
                    Maximum = samples.Length == 0 ? null : samples.Max()
                };
            })
            .ToArray();
        return new TimeWindowQualitySummary
        {
            ScopeCount = scopes.Count,
            InspectionCount = records.Length,
            PassCount = passCount,
            FailCount = failCount,
            PassRate = records.Length == 0 ? null : passCount / (double)records.Length,
            Characteristics = values
        };
    }

    private static IReadOnlyDictionary<string, string> ResolveContext(IReadOnlyList<PlatformProductionEvent> rows)
        => rows.Where(static row => row.Event.EventType == "process.execution.started")
               .Select(static row => row.Event.Context)
               .FirstOrDefault(static item => item.Count > 0)
           ?? rows.Select(static row => row.Event.Context).FirstOrDefault(static item => item.Count > 0)
           ?? new Dictionary<string, string>();

    private static void EnsureComparisonKeysPresent(
        IReadOnlyDictionary<string, string> context,
        IReadOnlyList<string> keys,
        string source)
    {
        var missing = keys
            .Where(key => string.IsNullOrWhiteSpace(ProcessAnalysisResolver.ContextValue(context, key)))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"{source}缺少同类比较上下文：{string.Join("、", missing)}。");
    }

    private static void EnsureComparisonKeysConsistent(
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<string> keys,
        string source)
    {
        var sampleContexts = rows
            .Where(static row => row.Event.EventType == "process.execution.started")
            .Select(static row => row.Event.Context)
            .ToArray();
        foreach (var key in keys)
        {
            var values = sampleContexts
                .Select(context => ProcessAnalysisResolver.ContextValue(context, key))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (values.Length > 1)
                throw new ArgumentException($"{source}包含多个 {key} 值，请缩小窗口或调整分析方案的同类比较键。");
        }
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        int maximumRows,
        CancellationToken ct)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var remaining = maximumRows - result.Count;
            var requestedLimit = Math.Min(500, remaining + 1);
            var page = await events.QueryAsync(
                query with { AfterIngestId = cursor, Limit = requestedLimit }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            if (page.Count > remaining)
                throw new PlatformEventQueryLimitExceededException(maximumRows);
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("分析窗口查询游标没有前进。");
            cursor = next;
            if (page.Count < requestedLimit)
                break;
        }
        return result;
    }
}
