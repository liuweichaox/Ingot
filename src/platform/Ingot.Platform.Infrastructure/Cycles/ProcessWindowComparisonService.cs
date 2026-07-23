using System.Collections;
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class ProcessWindowComparisonService(
    IPlatformEventStore events,
    ProcessAnalysisResolver analysisResolver,
    IInspectionRecordStore inspections) : IProcessWindowComparisonService
{
    public async Task<ProcessWindowComparisonResult> CompareAsync(
        ProcessWindowComparisonRequest request,
        CancellationToken ct = default)
    {
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
        foreach (var window in request.Windows)
        {
            rows[window.WindowId] = await QueryAllAsync(new PlatformEventQuery
            {
                SubjectType = window.SubjectType.Trim(),
                SubjectId = window.SubjectId.Trim(),
                From = window.From.ToUniversalTime(),
                To = window.To.ToUniversalTime()
            }, ct).ConfigureAwait(false);
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

        var scopes = await inspections.ListScopesAsync(ct).ConfigureAwait(false);
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
                await inspections.QueryAllByOperationRunIdsAsync(scopeIds, ct).ConfigureAwait(false));
        var resultRows = request.Windows.Select(window => BuildRow(
            window,
            rows[window.WindowId],
            analysis,
            scopesByWindow[window.WindowId],
            inspectionRecords)).ToArray();
        var baseline = resultRows.Single(item => item.WindowId == request.BaselineWindowId);
        return new ProcessWindowComparisonResult
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

    private static ProcessWindowComparisonRow BuildRow(
        ProcessAnalysisWindowSelection window,
        IReadOnlyList<PlatformProductionEvent> rows,
        ResolvedProcessAnalysis analysis,
        IReadOnlyList<InspectionScope> scopes,
        IReadOnlyList<InspectionRecord> inspectionRecords)
    {
        var samples = rows.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var items = analysis.DataModel.Acquisition.DataItems.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var selected = analysis.Plan.Signals.Where(signal => items.ContainsKey(signal.DataItemCode))
            .Select(signal => items[signal.DataItemCode]).ToArray();
        var buckets = selected.ToDictionary(static item => item.Code, static _ => new List<double>(), StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            if (!sample.Event.Data.TryGetValue("values", out var rawValues))
                continue;
            foreach (var item in selected)
            {
                if (TryReadNumber(rawValues, item.Code, out var value))
                    buckets[item.Code].Add(value);
            }
        }
        return new ProcessWindowComparisonRow
        {
            WindowId = window.WindowId,
            Label = window.Label,
            SubjectType = window.SubjectType,
            SubjectId = window.SubjectId,
            From = window.From.ToUniversalTime(),
            To = window.To.ToUniversalTime(),
            EventCount = rows.Count,
            SampleCount = samples.Length,
            Context = ResolveContext(rows),
            Quality = BuildQuality(scopes, inspectionRecords),
            Signals = selected.Select(item => new CycleSignalStatistic
            {
                Code = item.Code,
                Name = item.SourceField,
                Unit = item.Unit,
                SampleCount = buckets[item.Code].Count,
                Average = buckets[item.Code].Count == 0 ? null : buckets[item.Code].Average(),
                Minimum = buckets[item.Code].Count == 0 ? null : buckets[item.Code].Min(),
                Maximum = buckets[item.Code].Count == 0 ? null : buckets[item.Code].Max()
            }).ToArray()
        };
    }

    private static bool ScopeBelongsToWindow(
        InspectionScope scope,
        ProcessAnalysisWindowSelection window)
        => string.Equals(scope.ScopeId, window.WindowId, StringComparison.Ordinal) ||
           (string.Equals(scope.SubjectType, window.SubjectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(scope.SubjectId, window.SubjectId, StringComparison.OrdinalIgnoreCase) &&
            scope.From >= window.From.ToUniversalTime() &&
            scope.To <= window.To.ToUniversalTime());

    private static ProcessWindowQualitySummary BuildQuality(
        IReadOnlyList<InspectionScope> scopes,
        IReadOnlyList<InspectionRecord> allRecords)
    {
        var scopeIds = scopes.Select(static scope => scope.ScopeId).ToHashSet(StringComparer.Ordinal);
        var records = allRecords.Where(record => scopeIds.Contains(record.OperationRunId)).ToArray();
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
                return new ProcessWindowQualityCharacteristic
                {
                    Code = group.Key,
                    SampleCount = samples.Length,
                    Average = samples.Length == 0 ? null : samples.Average(),
                    Minimum = samples.Length == 0 ? null : samples.Min(),
                    Maximum = samples.Length == 0 ? null : samples.Max()
                };
            })
            .ToArray();
        return new ProcessWindowQualitySummary
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
        => rows.Where(static row => row.Event.EventType == "process.sample")
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
            .Where(static row => row.Event.EventType == "process.sample")
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

    private static bool TryReadNumber(object? container, string key, out double value)
    {
        value = 0;
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property) && property.TryGetDouble(out value))
            return true;
        if (container is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(key, out var raw) && TryConvert(raw, out value))
            return true;
        return container is IDictionary dictionary && dictionary.Contains(key) && TryConvert(dictionary[key], out value);
    }

    private static bool TryConvert(object? raw, out double value)
    {
        if (raw is JsonElement element && element.TryGetDouble(out value))
            return true;
        return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(PlatformEventQuery query, CancellationToken ct)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("分析窗口查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
