using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Inspections;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed record ResearchObservationAssembly(
    IReadOnlyList<ExperimentRunObservation> Observations,
    int CandidateRunCount)
{
    public int ValidObservationCount =>
        Observations.Count(static value => value.ValidForOptimization);
}

public interface IResearchObservationAssembler
{
    Task<ResearchObservationAssembly> AssembleAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        CancellationToken ct = default);
}

/// <summary>
///     将实验运行标识与 PLC 生产周期 CorrelationId 对齐，并把版本化周期特征和
///     有效检验记录投影成优化训练元组。RunKey 是唯一的接线键，不引入第二套
///     “优化观察”业务实体。
/// </summary>
public sealed class ResearchObservationAssembler(
    ICycleComparisonService cycles,
    IInspectionRecordStore inspections) : IResearchObservationAssembler
{
    private const int MaximumRunsPerAssembly = 2000;

    public async Task<ResearchObservationAssembly> AssembleAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        CancellationToken ct = default)
    {
        var candidates = experiments
            .Where(static experiment => experiment.Status != ResearchExperimentStatuses.Cancelled)
            .SelectMany(experiment => experiment.RunPlan.Select(run => new CandidateRun(experiment, run)))
            .GroupBy(static value => value.Run.RunKey, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static value => value.Experiment.UpdatedAt)
                .First())
            .OrderBy(static value => value.Experiment.CreatedAt)
            .ThenBy(static value => value.Run.Sequence)
            .ToArray();
        if (candidates.Length > MaximumRunsPerAssembly)
            throw new ProcessResearchRuleException(
                $"单次优化最多自动装配 {MaximumRunsPerAssembly} 个实验运行，请先归档历史项目。");

        var runKeys = candidates.Select(static item => item.Run.RunKey).ToArray();
        var cyclesByRun = await cycles.GetCyclesAsync(runKeys, ct).ConfigureAwait(false);
        var allRecords = InspectionRecordSet.Effective(
            await inspections.QueryAllByOperationRunIdsAsync(runKeys, ct).ConfigureAwait(false));
        var recordsByRun = allRecords
            .GroupBy(static item => item.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var observations = new List<ExperimentRunObservation>(candidates.Length);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!cyclesByRun.TryGetValue(candidate.Run.RunKey, out var cycle))
                continue;
            observations.Add(BuildObservation(
                project,
                candidate.Run,
                cycle,
                recordsByRun.GetValueOrDefault(candidate.Run.RunKey, [])));
        }
        return new ResearchObservationAssembly(observations, candidates.Length);
    }

    private static ExperimentRunObservation BuildObservation(
        ResearchProject project,
        ExperimentRunPlan run,
        CycleComparisonRow cycle,
        IReadOnlyList<InspectionRecord> records)
    {
        var recipeValues = cycle.RecipeParameters
            .Select(static value => (value.Code, Value: ReadNumber(value.Value)))
            .Where(static value => value.Value.HasValue)
            .ToDictionary(
                static value => value.Code,
                static value => value.Value!.Value,
                StringComparer.Ordinal);
        var plannedValues = run.Factors.ToDictionary(
            static value => value.VariableCode,
            static value => value.Value,
            StringComparer.Ordinal);
        var factors = new List<ExperimentFactorSetting>();
        var missing = new List<string>();
        foreach (var variable in project.Variables.Where(
                     static value => value.Role == ResearchVariableRoles.Control))
        {
            if (!TryResolveControlValue(variable, cycle, recipeValues, plannedValues, out var value))
            {
                missing.Add($"控制变量:{variable.Code}");
                continue;
            }
            factors.Add(new ExperimentFactorSetting
            {
                VariableCode = variable.Code,
                Value = value,
                Unit = variable.Unit
            });
        }

        var outcomes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var objective in project.Objectives)
        {
            var sourceCode = ResolveInspectionCode(objective.Code, objective.DataSource);
            if (TryResolveMeasurement(records, sourceCode, objective.Unit, out var value))
                outcomes[objective.Code] = value;
            else
                missing.Add($"目标:{objective.Code}");
        }
        var constraintOutcomes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var constraint in project.OutcomeConstraints)
        {
            var sourceCode = ResolveInspectionCode(constraint.OutcomeCode, constraint.DataSource);
            if (TryResolveMeasurement(records, sourceCode, constraint.Unit, out var value))
                constraintOutcomes[constraint.Code] = value;
            else
                missing.Add($"结果约束:{constraint.Code}");
        }

        var processFeatures = FlattenFeatures(cycle);
        var context = new Dictionary<string, string>(cycle.Context, StringComparer.Ordinal)
        {
            ["machine_id"] = cycle.MachineId
        };
        AddContext(context, "product_series", cycle.ProductSeries);
        AddContext(context, "product_code", cycle.ProductCode);
        AddContext(context, "recipe_id", cycle.RecipeId);
        AddContext(context, "recipe_version", cycle.RecipeVersion);
        AddContext(context, "tooling_installation_id", cycle.ToolingInstallationId);
        AddContext(context, "tooling_id", cycle.ToolingId);
        AddContext(context, "mold_id", cycle.MoldId);
        AddContext(context, "assembly_revision_id", cycle.AssemblyRevisionId);
        AddContext(context, "assembly_revision", cycle.AssemblyRevision);
        if (cycle.CompletedAt is null)
            missing.Add("周期未完成");
        if (cycle.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable)
            missing.Add("过程数据不可用");
        if (processFeatures.Count == 0)
            missing.Add("没有可用过程特征");
        var valid = missing.Count == 0;
        var hashPayload = new
        {
            cycle.CorrelationId,
            cycle.CompletedAt,
            cycle.AnalysisMaterialization.AlgorithmVersion,
            cycle.AnalysisMaterialization.SourceMaxIngestId,
            cycle.AnalysisMaterialization.SourceEventCount,
            Factors = factors,
            ProcessFeatures = processFeatures,
            Outcomes = outcomes,
            ConstraintOutcomes = constraintOutcomes,
            Context = context,
            Inspections = records
                .OrderBy(static value => value.RecordId)
                .Select(static value => new
                {
                    value.RecordId,
                    value.MeasuredAt,
                    value.Outcome,
                    value.SupersedesRecordId,
                    value.Measurements
                })
        };
        var contentHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hashPayload)));
        return new ExperimentRunObservation
        {
            RunKey = run.RunKey,
            Context = context,
            ActualFactors = factors,
            ProcessFeatures = processFeatures,
            Outcomes = outcomes,
            ConstraintOutcomes = constraintOutcomes,
            ValidForOptimization = valid,
            ExclusionReason = valid ? null : string.Join("；", missing),
            SourceContentHash = contentHash
        };
    }

    private static void AddContext(
        IDictionary<string, string> context,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            context[key] = value;
    }

    private static bool TryResolveControlValue(
        ResearchVariable variable,
        CycleComparisonRow cycle,
        IReadOnlyDictionary<string, double> recipeValues,
        IReadOnlyDictionary<string, double> plannedValues,
        out double value)
    {
        var source = variable.DataSource?.Trim();
        if (!string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("signal:", StringComparison.OrdinalIgnoreCase))
            return TryResolveSignalFeature(cycle, source, out value);
        if (!string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("recipe:", StringComparison.OrdinalIgnoreCase))
        {
            var configuredRecipeCode = source["recipe:".Length..].Trim();
            return recipeValues.TryGetValue(configuredRecipeCode, out value) &&
                   double.IsFinite(value);
        }
        var recipeCode = variable.Code;
        if (recipeValues.TryGetValue(recipeCode, out value) && double.IsFinite(value))
            return true;
        // 仅为未声明数据映射的历史项目保留计划值兼容；显式 recipe/signal 映射绝不降级。
        return plannedValues.TryGetValue(variable.Code, out value) && double.IsFinite(value);
    }

    private static bool TryResolveSignalFeature(
        CycleComparisonRow cycle,
        string source,
        out double value)
    {
        value = default;
        var parts = source.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4)
            return false;
        var signal = cycle.Signals.FirstOrDefault(
            item => string.Equals(item.Code, parts[1], StringComparison.Ordinal));
        if (signal is null)
            return false;
        var matches = signal.Features.Where(feature =>
            string.Equals(feature.Code, parts[2], StringComparison.Ordinal) &&
            (parts.Length == 3
                ? feature.PhaseCode is null
                : string.Equals(feature.PhaseCode, parts[3], StringComparison.Ordinal)));
        var feature = matches
            .OrderBy(static item => item.PhaseOrder ?? 0)
            .FirstOrDefault(static item => item.Value.HasValue);
        if (feature?.Value is not { } resolved || !double.IsFinite(resolved))
            return false;
        value = resolved;
        return true;
    }

    private static Dictionary<string, double> FlattenFeatures(CycleComparisonRow cycle)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var signal in cycle.Signals.OrderBy(static value => value.Code, StringComparer.Ordinal))
        {
            if (signal.Average is { } average && double.IsFinite(average))
                result[$"{signal.Code}.average"] = average;
            foreach (var feature in signal.Features
                         .Where(static value => value.Value.HasValue)
                         .OrderBy(static value => value.Code, StringComparer.Ordinal)
                         .ThenBy(static value => value.PhaseCode, StringComparer.Ordinal)
                         .ThenBy(static value => value.PhaseOrder))
            {
                if (!double.IsFinite(feature.Value!.Value))
                    continue;
                var phase = feature.PhaseCode is null
                    ? "cycle"
                    : $"{feature.PhaseCode}[{feature.PhaseOrder ?? 1}]";
                result[$"{signal.Code}.{phase}.{feature.Code}"] = feature.Value.Value;
            }
        }
        return result;
    }

    private static string ResolveInspectionCode(string fallback, string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            return fallback;
        var source = dataSource.Trim();
        return source.StartsWith("inspection:", StringComparison.OrdinalIgnoreCase)
            ? source["inspection:".Length..].Trim()
            : source;
    }

    private static bool TryResolveMeasurement(
        IReadOnlyList<InspectionRecord> records,
        string characteristicCode,
        string expectedUnit,
        out double value)
    {
        value = default;
        var match = records
            .OrderByDescending(static record => record.MeasuredAt)
            .ThenByDescending(static record => record.IngestedAt)
            .SelectMany(record => record.Measurements.Select(measurement => (record, measurement)))
            .FirstOrDefault(item =>
                string.Equals(
                    item.measurement.CharacteristicCode,
                    characteristicCode,
                    StringComparison.Ordinal) &&
                item.measurement.NumericValue.HasValue &&
                (string.IsNullOrWhiteSpace(item.measurement.Unit) ||
                 string.Equals(item.measurement.Unit, expectedUnit, StringComparison.OrdinalIgnoreCase)));
        if (match.measurement?.NumericValue is not { } numeric)
            return false;
        value = decimal.ToDouble(numeric);
        return double.IsFinite(value);
    }

    private static double? ReadNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
            double.IsFinite(number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out number) &&
            double.IsFinite(number))
            return number;
        return null;
    }

    private sealed record CandidateRun(
        ResearchExperiment Experiment,
        ExperimentRunPlan Run);
}
