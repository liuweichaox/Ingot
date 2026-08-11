using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisEngine(
    IFeatureDefinitionRegistry? featureDefinitions = null)
{
    public static readonly string AlgorithmVersion =
        $"stage-relative-v5+{ProcessExecutionAnalysisThresholds.ComputeFingerprint()}";
    private readonly IFeatureDefinitionRegistry _featureDefinitions =
        featureDefinitions ?? new BuiltInFeatureDefinitionRegistry();

    public WholeProcessExecutionAnalysisResult Analyze(
        IReadOnlyList<PlatformProductionEvent> rows,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan)
    {
        var samplesByIngest = rows
            .Where(static row => row.Event.EventType == "process.sample")
            .OrderBy(static row => row.IngestId)
            .ToArray();
        var outOfOrderCount = samplesByIngest
            .Zip(samplesByIngest.Skip(1))
            .Count(static pair => pair.Second.Event.OccurredAt < pair.First.Event.OccurredAt);
        var duplicateTimestampCount = samplesByIngest
            .GroupBy(static row => row.Event.OccurredAt)
            .Sum(static group => Math.Max(0, group.Count() - 1));
        var samples = samplesByIngest
            .GroupBy(static row => row.Event.OccurredAt)
            .Select(static group => group.OrderByDescending(static row => row.IngestId).First())
            .OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var intervals = samples
            .Zip(samples.Skip(1))
            .Select(static pair => (pair.Second.Event.OccurredAt - pair.First.Event.OccurredAt).TotalMilliseconds)
            .Where(static value => value >= 0)
            .ToArray();
        var medianInterval = Percentile(intervals, 0.5);
        var p95Interval = Percentile(intervals, 0.95);
        double? maximumGap = intervals.Length == 0 ? null : intervals.Max();
        var interruptionThreshold = medianInterval is > 0 ? medianInterval.Value * 5d : double.PositiveInfinity;
        var sequenceGapCount = CountSourceSequenceGaps(samples);
        var sourceClockOffsets = samplesByIngest
            .Select(static row => (row.Event.RecordedAt - row.Event.OccurredAt).TotalMilliseconds)
            .Where(double.IsFinite)
            .ToArray();
        var platformIngestLatencies = samplesByIngest
            .Select(static row => (row.IngestedAt - row.Event.RecordedAt).TotalMilliseconds)
            .Where(double.IsFinite)
            .ToArray();
        var medianSourceClockOffset = Percentile(sourceClockOffsets, 0.5);
        double? maximumAbsoluteSourceClockOffset = sourceClockOffsets.Length == 0
            ? null
            : sourceClockOffsets.Max(static value => Math.Abs(value));
        var medianPlatformIngestLatency = Percentile(platformIngestLatencies, 0.5);
        var p95PlatformIngestLatency = Percentile(platformIngestLatencies, 0.95);
        double? maximumPlatformIngestLatency = platformIngestLatencies.Length == 0
            ? null
            : platformIngestLatencies.Max();
        var negativePlatformIngestLatencyCount = platformIngestLatencies.Count(static value => value < -1000d);
        var phaseAnalysis = BuildPhases(samples, completedAt, dataModel, plan);

        var definitions = dataModel?.Acquisition.DataItems
            .ToDictionary(static item => item.Code, StringComparer.Ordinal)
            ?? new Dictionary<string, ProcessDataItemDefinition>(StringComparer.Ordinal);
        var selections = plan?.Signals
            .Where(selection => definitions.ContainsKey(selection.DataItemCode))
            .ToArray() ?? [];
        var signalCoverage = selections.Select(selection =>
        {
            var valid = samples.Count(sample => TryReadNumber(sample.Event.Data, selection.DataItemCode, out _));
            return new SignalDataCoverage
            {
                Code = selection.DataItemCode,
                ValidSampleCount = valid,
                Coverage = samples.Length == 0 ? 0 : valid / (double)samples.Length
            };
        }).ToArray();

        var issues = new List<string>();
        var status = ProcessDataStatuses.Available;
        if (!startedAt.HasValue || !completedAt.HasValue || completedAt <= startedAt)
        {
            status = ProcessDataStatuses.Unavailable;
            issues.Add("过程执行缺少有效的开始或结束时间。");
        }
        if (samples.Length == 0)
        {
            status = ProcessDataStatuses.Unavailable;
            issues.Add("过程执行内没有过程采样。");
        }
        if (selections.Length > 0 && signalCoverage.All(static item => item.ValidSampleCount == 0))
        {
            status = ProcessDataStatuses.Unavailable;
            issues.Add("分析方案选择的信号均没有有效数值。");
        }
        if (duplicateTimestampCount > 0)
        {
            status = Degrade(status);
            issues.Add($"发现 {duplicateTimestampCount} 条重复时间戳，特征计算保留最后摄入值。");
        }
        if (outOfOrderCount > 0)
            issues.Add($"发现 {outOfOrderCount} 条晚到或乱序采样，已按源时间排序。");
        if (sequenceGapCount > 0)
        {
            status = Degrade(status);
            issues.Add($"发现 {sequenceGapCount} 个源采样序号间断。");
        }
        if (maximumGap.HasValue && maximumGap.Value > interruptionThreshold)
        {
            status = Degrade(status);
            issues.Add($"最大采样空窗 {maximumGap.Value:F1}ms，超过常态间隔的 5 倍。");
        }
        foreach (var signal in signalCoverage.Where(static item =>
                     item.Coverage < ProcessExecutionAnalysisThresholds.MinimumSignalCoverage))
        {
            status = Degrade(status);
            issues.Add($"信号 {signal.Code} 有效覆盖率为 {signal.Coverage:P1}。");
        }
        if (phaseAnalysis.Issues.Count > 0)
        {
            status = Degrade(status);
            issues.AddRange(phaseAnalysis.Issues);
        }

        var quality = new ProcessDataQualitySummary
        {
            Status = status,
            SampleCount = samples.Length,
            MedianIntervalMs = medianInterval,
            P95IntervalMs = p95Interval,
            MaximumGapMs = maximumGap,
            DuplicateTimestampCount = duplicateTimestampCount,
            OutOfOrderCount = outOfOrderCount,
            SequenceGapCount = sequenceGapCount,
            MedianSourceClockOffsetMs = medianSourceClockOffset,
            MaximumAbsoluteSourceClockOffsetMs = maximumAbsoluteSourceClockOffset,
            MedianPlatformIngestLatencyMs = medianPlatformIngestLatency,
            P95PlatformIngestLatencyMs = p95PlatformIngestLatency,
            MaximumPlatformIngestLatencyMs = maximumPlatformIngestLatency,
            NegativePlatformIngestLatencyCount = negativePlatformIngestLatencyCount,
            Signals = signalCoverage,
            Issues = issues
        };
        var signals = selections.Select(selection => BuildSignal(
            samples,
            startedAt,
            completedAt,
            definitions[selection.DataItemCode],
            selection,
            interruptionThreshold,
            phaseAnalysis.Phases)).ToArray();
        return new WholeProcessExecutionAnalysisResult(quality, signals, phaseAnalysis.Phases);
    }

    private ProcessSignalStatistic BuildSignal(
        IReadOnlyList<PlatformProductionEvent> samples,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataItemDefinition definition,
        AnalysisSignalSelection selection,
        double interruptionThreshold,
        IReadOnlyList<ProcessPhaseSummary> phases)
    {
        var points = samples
            .Select(sample => TryReadNumber(sample.Event.Data, definition.Code, out var value)
                ? new SignalPoint(sample.Event.OccurredAt, value)
                : null)
            .Where(static point => point is not null)
            .Cast<SignalPoint>()
            .ToArray();
        var requested = selection.Features.Count == 0
            ? new[] { "mean", "min", "max" }
            : selection.Features.Select(static value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        var definitions = requested.Select(_featureDefinitions.GetRequired).ToArray();
        var wholeProcessExecutionFeatures = definitions.Select(definition => BuildFeature(
            definition,
            points,
            startedAt,
            completedAt,
            interruptionThreshold,
            null)).ToArray();
        var phaseFeatures = phases
            .Where(static phase => phase.StartedAt.HasValue && phase.EndedAt.HasValue)
            .SelectMany(phase => definitions.Select(definition => BuildFeature(
                definition,
                points,
                phase.StartedAt,
                phase.EndedAt,
                interruptionThreshold,
                phase)))
            .ToArray();
        var features = wholeProcessExecutionFeatures.Concat(phaseFeatures).ToArray();
        return new ProcessSignalStatistic
        {
            Code = definition.Code,
            Name = definition.DisplayName,
            Unit = definition.Unit,
            SampleCount = points.Length,
            Average = FeatureValue(wholeProcessExecutionFeatures, "mean", "average"),
            Minimum = FeatureValue(wholeProcessExecutionFeatures, "min", "minimum"),
            Maximum = FeatureValue(wholeProcessExecutionFeatures, "max", "maximum"),
            ValidDurationMs = wholeProcessExecutionFeatures.FirstOrDefault()?.ValidDurationMs ?? 0,
            Coverage = wholeProcessExecutionFeatures.FirstOrDefault()?.Coverage ?? 0,
            Features = features
        };
    }

    private static ProcessSignalFeature BuildFeature(
        ProcessFeatureDefinition definition,
        IReadOnlyList<SignalPoint> allPoints,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        double interruptionThreshold,
        ProcessPhaseSummary? phase)
    {
        var strictPoints = startedAt.HasValue && endedAt.HasValue
            ? allPoints.Where(point => point.At >= startedAt.Value && point.At < endedAt.Value).ToArray()
            : allPoints.ToArray();
        var trailingBoundaryPoint = endedAt.HasValue
            ? allPoints.FirstOrDefault(point => point.At == endedAt.Value)
            : null;
        var calculationPoints = trailingBoundaryPoint is not null &&
                                !strictPoints.Any(point => point.At == trailingBoundaryPoint.At)
            ? strictPoints.Concat([trailingBoundaryPoint]).ToArray()
            : strictPoints;
        var segments = calculationPoints.Zip(calculationPoints.Skip(1))
            .Select(static pair => new SignalSegment(
                pair.First,
                pair.Second,
                (pair.Second.At - pair.First.At).TotalMilliseconds))
            .Where(segment => segment.DurationMs > 0 && segment.DurationMs <= interruptionThreshold)
            .ToArray();
        var validDurationMs = segments.Sum(static segment => segment.DurationMs);
        var scopeDurationMs = startedAt.HasValue && endedAt > startedAt
            ? (endedAt.Value - startedAt.Value).TotalMilliseconds
            : validDurationMs;
        var coverage = scopeDurationMs <= 0 ? 0 : Math.Clamp(validDurationMs / scopeDurationMs, 0, 1);
        var values = strictPoints.Select(static point => point.Value).ToArray();
        var mean = TimeWeightedMean(segments);
        return new ProcessSignalFeature
        {
            Code = definition.Code,
            DefinitionVersion = definition.Version,
            DefinitionHash = definition.DefinitionHash,
            ComputationHash = ComputeFeatureHash(
                definition,
                calculationPoints,
                startedAt,
                endedAt,
                interruptionThreshold),
            InputPointCount = calculationPoints.Length,
            PhaseCode = phase?.Code,
            PhaseName = phase?.Name,
            PhaseOrder = phase?.Order,
            PhaseSource = phase?.Source ?? "execution",
            StartedAt = phase?.StartedAt,
            EndedAt = phase?.EndedAt,
            Value = definition.Operator switch
            {
                "time_weighted_mean" => mean,
                "minimum" => values.Length == 0 ? null : values.Min(),
                "maximum" => values.Length == 0 ? null : values.Max(),
                "range" => values.Length == 0 ? null : values.Max() - values.Min(),
                "time_weighted_standard_deviation" => TimeWeightedStandardDeviation(segments, mean),
                "weighted_percentile_50" => WeightedPercentile(calculationPoints, segments, 0.5),
                "weighted_percentile_05" => WeightedPercentile(calculationPoints, segments, 0.05),
                "weighted_percentile_95" => WeightedPercentile(calculationPoints, segments, 0.95),
                "trapezoid_integral" => TrapezoidIntegral(segments),
                "weighted_linear_slope" => WeightedSlope(calculationPoints, segments, startedAt),
                _ => null
            },
            ValidDurationMs = validDurationMs,
            Coverage = coverage
        };
    }

    private static string ComputeFeatureHash(
        ProcessFeatureDefinition definition,
        IReadOnlyList<SignalPoint> points,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        double interruptionThreshold)
    {
        var canonical = new StringBuilder()
            .Append(definition.DefinitionHash).Append('|')
            .Append(startedAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(endedAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(interruptionThreshold.ToString("R", CultureInfo.InvariantCulture));
        foreach (var point in points)
        {
            canonical.Append('|')
                .Append(point.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(point.Value.ToString("R", CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static PhaseAnalysisResult BuildPhases(
        IReadOnlyList<PlatformProductionEvent> samples,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan)
    {
        if (dataModel is null ||
            !string.Equals(plan?.AlignmentMode, "stage-relative", StringComparison.Ordinal))
            return new PhaseAnalysisResult([], []);

        var stageNumberItem = dataModel.Acquisition.DataItems
            .SingleOrDefault(static item => item.Category == "stage");
        if (stageNumberItem is null)
            return new PhaseAnalysisResult([], []);

        var resolved = samples.Select(sample =>
        {
            var stageNumber = ProcessAnalysisResolver.ResolveStage(
                sample.Event.Context,
                sample.Event.Data,
                dataModel);
            if (string.IsNullOrWhiteSpace(stageNumber))
            {
                return new ResolvedPhaseSample(
                    sample.Event.OccurredAt,
                    "unknown",
                    "阶段号缺失",
                    "unknown");
            }
            var stageName = ProcessAnalysisResolver.ContextValue(sample.Event.Context, "process_stage_name")
                            ?? ProcessAnalysisResolver.ContextValue(sample.Event.Context, "process_step_name")
                            ?? $"阶段 {stageNumber}";
            return stageNumber == "unknown"
                ? new ResolvedPhaseSample(sample.Event.OccurredAt, "unknown", "未归属", "unknown")
                : new ResolvedPhaseSample(
                    sample.Event.OccurredAt,
                    stageNumber,
                    stageName,
                    "stage_number");
        }).ToArray();
        if (resolved.Length == 0)
            return new PhaseAnalysisResult([], []);

        var groups = new List<List<ResolvedPhaseSample>>();
        foreach (var item in resolved)
        {
            if (groups.Count == 0 ||
                groups[^1][^1].Code != item.Code ||
                groups[^1][^1].Source != item.Source)
                groups.Add([]);
            groups[^1].Add(item);
        }

        var phases = groups.Select((group, index) =>
        {
            var nextStartedAt = index + 1 < groups.Count ? groups[index + 1][0].At : completedAt;
            return new ProcessPhaseSummary
            {
                Code = group[0].Code,
                Name = group[0].Name,
                Order = index + 1,
                Source = group[0].Source,
                SampleCount = group.Count,
                StartedAt = group[0].At,
                EndedAt = nextStartedAt
            };
        }).ToArray();
        return new PhaseAnalysisResult(phases, []);
    }

    private static string Degrade(string current)
        => current == ProcessDataStatuses.Unavailable ? current : ProcessDataStatuses.Degraded;

    private static int CountSourceSequenceGaps(IReadOnlyList<PlatformProductionEvent> samples)
    {
        var sequences = samples
            .Select(static sample => ReadLong(sample.Event.Data, "sourceSequence") ??
                                     ReadLong(sample.Event.Data, "source_sequence"))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .Distinct()
            .Order()
            .ToArray();
        return sequences.Zip(sequences.Skip(1)).Count(static pair => pair.Second > pair.First + 1);
    }

    private static double? TimeWeightedMean(IReadOnlyList<SignalSegment> segments)
    {
        var duration = segments.Sum(static item => item.DurationMs);
        return duration <= 0
            ? null
            : segments.Sum(static item =>
                (item.Start.Value + item.End.Value) * 0.5d * item.DurationMs) / duration;
    }

    private static double? TrapezoidIntegral(IReadOnlyList<SignalSegment> segments)
        => segments.Count == 0
            ? null
            : segments.Sum(static item =>
                (item.Start.Value + item.End.Value) * 0.5d * (item.DurationMs / 1000d));

    private static double? TimeWeightedStandardDeviation(
        IReadOnlyList<SignalSegment> segments,
        double? mean)
    {
        if (!mean.HasValue)
            return null;
        var duration = segments.Sum(static item => item.DurationMs);
        if (duration <= 0)
            return null;
        // Integrate the squared distance from the already-computed mean instead of
        // subtracting E[x]^2 from E[x^2]. The latter loses most significant digits
        // for common industrial signals with a large offset and small variation,
        // and made the C# and PostgreSQL implementations disagree solely because
        // their aggregate summation order differs.
        var centeredSquaredIntegral = segments.Sum(item =>
        {
            var start = item.Start.Value - mean.Value;
            var end = item.End.Value - mean.Value;
            return (start * start + start * end + end * end) / 3d * item.DurationMs;
        });
        return Math.Sqrt(Math.Max(0, centeredSquaredIntegral / duration));
    }

    private static double? WeightedPercentile(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<SignalSegment> segments,
        double percentile)
    {
        if (points.Count == 0)
            return null;
        if (segments.Count == 0)
            return points.Count == 1 ? points[0].Value : Percentile(points.Select(static item => item.Value).ToArray(), percentile);
        var weights = points.ToDictionary(static point => point.At, static _ => 0d);
        foreach (var segment in segments)
        {
            weights[segment.Start.At] += segment.DurationMs / 2d;
            weights[segment.End.At] += segment.DurationMs / 2d;
        }
        var weighted = points
            .Select(point => new { point.Value, Weight = weights[point.At] })
            .Where(static item => item.Weight > 0)
            .OrderBy(static item => item.Value)
            .ToArray();
        var total = weighted.Sum(static item => item.Weight);
        var target = total * percentile;
        var cumulative = 0d;
        foreach (var item in weighted)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
                return item.Value;
        }
        return weighted[^1].Value;
    }

    private static double? WeightedSlope(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<SignalSegment> segments,
        DateTimeOffset? startedAt)
    {
        if (points.Count < 2 || !startedAt.HasValue || segments.Count == 0)
            return null;
        var weights = points.ToDictionary(static point => point.At, static _ => 0d);
        foreach (var segment in segments)
        {
            weights[segment.Start.At] += segment.DurationMs / 2d;
            weights[segment.End.At] += segment.DurationMs / 2d;
        }
        var rows = points.Select(point => new
        {
            X = (point.At - startedAt.Value).TotalSeconds,
            Y = point.Value,
            Weight = weights[point.At]
        }).Where(static item => item.Weight > 0).ToArray();
        var totalWeight = rows.Sum(static item => item.Weight);
        if (totalWeight <= 0)
            return null;
        var meanX = rows.Sum(static item => item.X * item.Weight) / totalWeight;
        var meanY = rows.Sum(static item => item.Y * item.Weight) / totalWeight;
        var denominator = rows.Sum(item => item.Weight * Math.Pow(item.X - meanX, 2));
        return denominator <= 0
            ? null
            : rows.Sum(item => item.Weight * (item.X - meanX) * (item.Y - meanY)) / denominator;
    }

    private static double? FeatureValue(
        IReadOnlyList<ProcessSignalFeature> features,
        params string[] codes)
        => features.FirstOrDefault(item => codes.Contains(item.Code, StringComparer.Ordinal))?.Value;

    private static double? Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
            return null;
        var ordered = values.Order().ToArray();
        var position = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? ordered[lower]
            : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    private static bool TryReadNumber(
        IReadOnlyDictionary<string, object?> data,
        string key,
        out double value)
    {
        value = 0;
        if (!data.TryGetValue("values", out var container))
            return false;
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property) && property.TryGetDouble(out value))
            return double.IsFinite(value);
        if (container is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(key, out var raw) && TryConvert(raw, out value))
            return double.IsFinite(value);
        return container is IDictionary dictionary && dictionary.Contains(key) &&
               TryConvert(dictionary[key], out value) && double.IsFinite(value);
    }

    private static bool TryConvert(object? raw, out double value)
    {
        if (raw is JsonElement element && element.TryGetDouble(out value))
            return true;
        return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }

    private static long? ReadLong(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw))
            return null;
        if (raw is JsonElement element && element.TryGetInt64(out var parsed))
            return parsed;
        return long.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out parsed) ? parsed : null;
    }

    private sealed record SignalPoint(DateTimeOffset At, double Value);
    private sealed record SignalSegment(SignalPoint Start, SignalPoint End, double DurationMs);
    private sealed record ResolvedPhaseSample(
        DateTimeOffset At,
        string Code,
        string Name,
        string Source);
    private sealed record PhaseAnalysisResult(
        IReadOnlyList<ProcessPhaseSummary> Phases,
        IReadOnlyList<string> Issues);
}

public sealed record WholeProcessExecutionAnalysisResult(
    ProcessDataQualitySummary Quality,
    IReadOnlyList<ProcessSignalStatistic> Signals,
    IReadOnlyList<ProcessPhaseSummary> Phases);
