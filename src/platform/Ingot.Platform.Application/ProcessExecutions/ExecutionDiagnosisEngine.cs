using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>
///     过程执行级观察性诊断引擎。它统一评估实际控制参数与过程轨迹特征，
///     只输出待验证的候选原因，不把相关性升级为因果结论。
/// </summary>
public sealed class ExecutionDiagnosisEngine
{
    public const string AlgorithmVersion = "robust-stratified-v1";

    public ExecutionDiagnosisSummary Analyze(IReadOnlyList<ExecutionComparisonRow> rows)
    {
        var passRows = rows.Where(IsPass).ToArray();
        var failRows = rows.Where(IsFail).ToArray();
        var passWeight = passRows.Sum(static row => row.EvidenceWeight);
        var failWeight = failRows.Sum(static row => row.EvidenceWeight);
        if (passRows.Length == 0 || failRows.Length == 0)
        {
            return new ExecutionDiagnosisSummary
            {
                AlgorithmVersion = AlgorithmVersion,
                PassProcessExecutionCount = passRows.Length,
                FailProcessExecutionCount = failRows.Length,
                PassEffectiveWeight = passWeight,
                FailEffectiveWeight = failWeight,
                Limitations =
                [
                    "至少需要一个合格过程执行和一个不合格过程执行，才能筛选质量候选原因。",
                    "观察性关联不能替代受控实验。"
                ]
            };
        }

        var confounders = FindPossibleConfounders(passRows, failRows);
        var candidates = BuildProcessSpecificationCandidates(rows, passRows, failRows, confounders)
            .Concat(BuildProcessCandidates(rows, passRows, failRows, confounders))
            .Where(static candidate =>
                candidate.PassProcessExecutionCount > 0 &&
                candidate.FailProcessExecutionCount > 0 &&
                candidate.CandidateScore > 0)
            .OrderByDescending(static candidate => candidate.CandidateScore)
            .ThenBy(static candidate =>
                candidate.Actionability == ExecutionCauseActionability.Controllable ? 0 : 1)
            .ThenBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Take(100)
            .ToArray();

        return new ExecutionDiagnosisSummary
        {
            AlgorithmVersion = AlgorithmVersion,
            EvidenceLevel = passWeight >= 5 && failWeight >= 5
                ? "stable"
                : passWeight >= 2 && failWeight >= 2 ? "exploratory" : "insufficient",
            PassProcessExecutionCount = passRows.Length,
            FailProcessExecutionCount = failRows.Length,
            PassEffectiveWeight = passWeight,
            FailEffectiveWeight = failWeight,
            Candidates = candidates,
            Limitations =
            [
                "候选分数来自稳健的合格/不合格组间差异，不代表因果关系。",
                confounders.Count == 0
                    ? "当前比较未发现明显的离散上下文分布差异。"
                    : $"设备、产品、工艺规范或工装总成分布仍可能混杂结果：{string.Join("、", confounders)}。",
                "候选原因必须映射为可控变量并经过跨区组重复实验验证。"
            ]
        };
    }

    private static IEnumerable<ExecutionCauseCandidate> BuildProcessSpecificationCandidates(
        IReadOnlyList<ExecutionComparisonRow> rows,
        IReadOnlyList<ExecutionComparisonRow> passRows,
        IReadOnlyList<ExecutionComparisonRow> failRows,
        IReadOnlyList<string> confounders)
    {
        var parameters = rows
            .SelectMany(static row => row.ControlParameters)
            .Where(static parameter => ReadNumber(parameter.Value).HasValue)
            .GroupBy(static parameter => parameter.Code, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        foreach (var parameter in parameters)
        {
            var dataSource = $"control-parameter:{parameter.Code}";
            var candidate = BuildCandidate(
                passRows,
                failRows,
                row => row.ControlParameters
                    .FirstOrDefault(item => string.Equals(
                        item.Code,
                        parameter.Code,
                        StringComparison.Ordinal)) is { } value
                    ? ReadNumber(value.Value)
                    : null,
                confounders,
                new CandidateIdentity(
                    $"control-parameter:{parameter.Code}",
                    ExecutionCauseSourceKinds.ProcessSpecificationParameter,
                    ExecutionCauseActionability.Controllable,
                    parameter.Code,
                    dataSource,
                    parameter.Name ?? parameter.Code,
                    parameter.Unit));
            if (candidate is not null)
                yield return candidate;
        }
    }

    private static IEnumerable<ExecutionCauseCandidate> BuildProcessCandidates(
        IReadOnlyList<ExecutionComparisonRow> rows,
        IReadOnlyList<ExecutionComparisonRow> passRows,
        IReadOnlyList<ExecutionComparisonRow> failRows,
        IReadOnlyList<string> confounders)
    {
        var keys = rows.SelectMany(row => row.Signals.SelectMany(signal =>
                signal.Features.Where(static feature => feature.Value.HasValue)
                    .Select(feature => new ProcessFeatureKey(
                        signal.Code,
                        signal.Name,
                        signal.Unit,
                        feature.Code,
                        feature.PhaseCode,
                        feature.PhaseName,
                        feature.PhaseOrder))))
            .Distinct()
            .ToArray();
        foreach (var key in keys)
        {
            var phase = string.IsNullOrWhiteSpace(key.PhaseCode) ? null : $":{key.PhaseCode}";
            var dataSource = $"signal:{key.SignalCode}:{key.FeatureCode}{phase}";
            var candidate = BuildCandidate(
                passRows,
                failRows,
                row => row.Signals
                    .FirstOrDefault(signal => string.Equals(
                        signal.Code,
                        key.SignalCode,
                        StringComparison.Ordinal))?
                    .Features.FirstOrDefault(feature =>
                        string.Equals(feature.Code, key.FeatureCode, StringComparison.Ordinal) &&
                        string.Equals(feature.PhaseCode, key.PhaseCode, StringComparison.Ordinal) &&
                        feature.PhaseOrder == key.PhaseOrder)?.Value,
                confounders,
                new CandidateIdentity(
                    dataSource,
                    ExecutionCauseSourceKinds.ProcessFeature,
                    ExecutionCauseActionability.Observable,
                    key.SignalCode,
                    dataSource,
                    $"{key.SignalName} · {key.PhaseName ?? "全过程执行"} · {key.FeatureCode}",
                    key.Unit,
                    key.SignalCode,
                    key.FeatureCode,
                    key.PhaseCode,
                    key.PhaseName,
                    key.PhaseOrder));
            if (candidate is not null)
                yield return candidate;
        }
    }

    private static ExecutionCauseCandidate? BuildCandidate(
        IReadOnlyList<ExecutionComparisonRow> passRows,
        IReadOnlyList<ExecutionComparisonRow> failRows,
        Func<ExecutionComparisonRow, double?> selector,
        IReadOnlyList<string> confounders,
        CandidateIdentity identity)
    {
        var pass = Values(passRows, selector);
        var fail = Values(failRows, selector);
        if (pass.Length == 0 || fail.Length == 0)
            return null;
        var passMedian = WeightedPercentile(pass, 0.5);
        var failMedian = WeightedPercentile(fail, 0.5);
        var combined = pass.Concat(fail).ToArray();
        var combinedMedian = WeightedPercentile(combined, 0.5);
        var mad = combinedMedian.HasValue
            ? WeightedPercentile(
                combined.Select(item =>
                    new WeightedValue(Math.Abs(item.Value - combinedMedian.Value), item.Weight)).ToArray(),
                0.5)
            : null;
        var robustEffect = passMedian.HasValue && failMedian.HasValue && mad is > 0
            ? (double?)((failMedian.Value - passMedian.Value) / (1.4826d * mad.Value))
            : null;
        var relativeDifference = passMedian.HasValue && failMedian.HasValue
            ? (double?)((failMedian.Value - passMedian.Value) /
              Math.Max(Math.Max(Math.Abs(passMedian.Value), Math.Abs(failMedian.Value)), 1e-9d))
            : null;
        var passWeight = pass.Sum(static item => item.Weight);
        var failWeight = fail.Sum(static item => item.Weight);
        var support = Math.Min(1d, Math.Min(passWeight, failWeight) / 5d);
        return new ExecutionCauseCandidate
        {
            CandidateId = identity.CandidateId,
            SourceKind = identity.SourceKind,
            Actionability = identity.Actionability,
            VariableCode = identity.VariableCode,
            DataSource = identity.DataSource,
            DisplayName = identity.DisplayName,
            Unit = identity.Unit,
            SignalCode = identity.SignalCode,
            FeatureCode = identity.FeatureCode,
            PhaseCode = identity.PhaseCode,
            PhaseName = identity.PhaseName,
            PhaseOrder = identity.PhaseOrder,
            PassProcessExecutionCount = pass.Length,
            FailProcessExecutionCount = fail.Length,
            PassEffectiveWeight = passWeight,
            FailEffectiveWeight = failWeight,
            PassMedian = passMedian,
            FailMedian = failMedian,
            MedianDifference = passMedian.HasValue && failMedian.HasValue
                ? failMedian.Value - passMedian.Value
                : null,
            RobustEffect = robustEffect,
            CandidateScore = Math.Abs(robustEffect ?? relativeDifference ?? 0) * support,
            EvidenceLevel = passWeight >= 5 && failWeight >= 5
                ? "stable"
                : passWeight >= 2 && failWeight >= 2 ? "exploratory" : "insufficient",
            PossibleConfounders = confounders
        };
    }

    private static WeightedValue[] Values(
        IReadOnlyList<ExecutionComparisonRow> rows,
        Func<ExecutionComparisonRow, double?> selector)
        => rows.Select(row =>
            {
                var value = selector(row);
                return value.HasValue && double.IsFinite(value.Value)
                    ? new WeightedValue(value.Value, row.EvidenceWeight)
                    : null;
            })
            .Where(static item => item is not null)
            .Cast<WeightedValue>()
            .ToArray();

    private static bool IsPass(ExecutionComparisonRow row)
        => row.EvidenceWeight > 0 &&
           row.InspectionOutcomes.Contains("PASS", StringComparer.Ordinal) &&
           !row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal);

    private static bool IsFail(ExecutionComparisonRow row)
        => row.EvidenceWeight > 0 &&
           row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal);

    private static IReadOnlyList<string> FindPossibleConfounders(
        IReadOnlyList<ExecutionComparisonRow> passRows,
        IReadOnlyList<ExecutionComparisonRow> failRows)
    {
        var result = new List<string>();
        AddIfDifferent("product_code", static row => row.ProductCode);
        AddIfDifferent("equipment_id", static row => row.EquipmentId);
        AddIfDifferent("process_specification", static row => $"{row.ProcessSpecificationId}@{row.ProcessSpecificationVersion}");
        AddIfDifferent("tooling_assembly_id", static row => row.ToolingAssemblyId);
        return result;

        void AddIfDifferent(string name, Func<ExecutionComparisonRow, string?> selector)
        {
            var pass = passRows.Select(selector)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var fail = failRows.Select(selector)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            if (pass.Count > 0 && fail.Count > 0 && !pass.SetEquals(fail))
                result.Add(name);
        }
    }

    private static double? WeightedPercentile(
        IReadOnlyList<WeightedValue> values,
        double percentile)
    {
        if (values.Count == 0)
            return null;
        var ordered = values.OrderBy(static item => item.Value).ToArray();
        var target = ordered.Sum(static item => item.Weight) * percentile;
        var cumulative = 0d;
        foreach (var item in ordered)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
                return item.Value;
        }
        return ordered[^1].Value;
    }

    private static double? ReadNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number) &&
            double.IsFinite(number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number) &&
            double.IsFinite(number))
            return number;
        return null;
    }

    private sealed record WeightedValue(double Value, double Weight);

    private sealed record CandidateIdentity(
        string CandidateId,
        string SourceKind,
        string Actionability,
        string VariableCode,
        string DataSource,
        string DisplayName,
        string? Unit,
        string? SignalCode = null,
        string? FeatureCode = null,
        string? PhaseCode = null,
        string? PhaseName = null,
        int? PhaseOrder = null);

    private sealed record ProcessFeatureKey(
        string SignalCode,
        string SignalName,
        string? Unit,
        string FeatureCode,
        string? PhaseCode,
        string? PhaseName,
        int? PhaseOrder);
}
