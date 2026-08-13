using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

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
///     将实验运行标识与 PLC 生产过程执行 ExecutionId 对齐，并把版本化过程执行特征和
///     有效检验记录投影成优化训练元组。ExecutionKey 是唯一的接线键，不引入第二套
///     “优化观察”业务实体。
/// </summary>
public sealed class ResearchObservationAssembler(
    IExecutionComparisonService executions,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore inspectionMasterData,
    IProcessConfigurationStore? processConfigurations = null,
    ResearchContextAdmissionEvaluator? contextAdmission = null) : IResearchObservationAssembler
{
    private const int MaximumRunsPerAssembly = 2000;
    private readonly ResearchContextAdmissionEvaluator _contextAdmission =
        contextAdmission ?? new ResearchContextAdmissionEvaluator();

    public async Task<ResearchObservationAssembly> AssembleAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        CancellationToken ct = default)
    {
        var scenarioPackage = await ResolveContextPolicyAsync(project, ct).ConfigureAwait(false);
        var candidates = experiments
            .Where(static experiment => experiment.Status != ResearchExperimentStatuses.Cancelled)
            .Where(static experiment =>
                experiment.Optimization?.Mode != ResearchOptimizationModes.Shadow)
            .SelectMany(experiment => experiment.RunPlan.Select(run => new CandidateRun(experiment, run)))
            .GroupBy(static value => value.Run.ExecutionKey, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static value => value.Experiment.UpdatedAt)
                .First())
            .OrderBy(static value => value.Experiment.CreatedAt)
            .ThenBy(static value => value.Run.Sequence)
            .ToArray();
        if (candidates.Length > MaximumRunsPerAssembly)
            throw new ProcessResearchRuleException(
                $"单次优化最多自动装配 {MaximumRunsPerAssembly} 个实验运行，请先归档历史项目。");

        var executionKeys = candidates.Select(static item => item.Run.ExecutionKey).ToArray();
        var executionsByRun = await executions.GetProcessExecutionsAsync(executionKeys, ct).ConfigureAwait(false);
        var allRecords = InspectionRecordSet.Effective(
            await inspections.QueryAllByExecutionIdsAsync(executionKeys, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allRecords.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var inspectionPlans = await inspectionMasterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var recordsByRun = allRecords
            .GroupBy(static item => item.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var observations = new List<ExperimentRunObservation>(candidates.Length);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!executionsByRun.TryGetValue(candidate.Run.ExecutionKey, out var execution))
                continue;
            var inspectionPlan = InspectionPlanMatcher.Resolve(
                inspectionPlans,
                execution.Context,
                execution.EquipmentId,
                execution.StartedAt);
            var eligibleRecords = InspectionRecordSet.AnalysisEligible(
                recordsByRun.GetValueOrDefault(candidate.Run.ExecutionKey, []),
                inspectionPlan,
                latestReviews);
            observations.Add(BuildObservation(
                project,
                candidate.Run,
                execution,
                eligibleRecords,
                scenarioPackage));
        }
        return new ResearchObservationAssembly(
            ApplyCohortContextGates(observations, scenarioPackage),
            candidates.Length);
    }

    private static IReadOnlyList<ExperimentRunObservation> ApplyCohortContextGates(
        IReadOnlyList<ExperimentRunObservation> observations,
        ScenarioPackage? scenarioPackage)
    {
        if (scenarioPackage is null || observations.Count == 0)
            return observations;
        var reasons = new List<string>();
        foreach (var field in scenarioPackage.ContextFields.OrderBy(static value => value.FieldCode, StringComparer.Ordinal))
        {
            var populated = observations.Where(value =>
                value.Context.TryGetValue(field.FieldCode, out var contextValue) &&
                !string.IsNullOrWhiteSpace(contextValue)).ToArray();
            var coverage = (double)populated.Length / observations.Count;
            if (field.MinimumCoverage.HasValue && coverage + 1e-12 < field.MinimumCoverage.Value)
                reasons.Add($"上下文字段 {field.FieldCode} 覆盖率 {coverage:P1} 低于门槛 {field.MinimumCoverage.Value:P1}");
            if (!field.MinimumFactorOverlap.HasValue || populated.Length == 0)
                continue;
            var factorLevels = populated.Select(FactorSignature).Distinct(StringComparer.Ordinal).ToArray();
            var contextLevels = populated.Select(value => value.Context[field.FieldCode]).Distinct(StringComparer.Ordinal).ToArray();
            var combinations = populated.Select(value => $"{FactorSignature(value)}|{value.Context[field.FieldCode]}")
                .Distinct(StringComparer.Ordinal).Count();
            var overlap = (double)combinations / (factorLevels.Length * contextLevels.Length);
            if (overlap + 1e-12 < field.MinimumFactorOverlap.Value)
                reasons.Add($"上下文字段 {field.FieldCode} 的因素组合重叠 {overlap:P1} 低于门槛 {field.MinimumFactorOverlap.Value:P1}");
        }
        if (reasons.Count == 0)
            return observations;
        var reason = string.Join("；", reasons);
        return observations.Select(value => value with
        {
            ValidForOptimization = false,
            ExclusionReason = string.IsNullOrWhiteSpace(value.ExclusionReason)
                ? reason
                : $"{value.ExclusionReason}；{reason}"
        }).ToArray();
    }

    private static string FactorSignature(ExperimentRunObservation observation)
        => string.Join('|', observation.ActualFactors.OrderBy(static value => value.VariableCode, StringComparer.Ordinal)
            .Select(static value => $"{value.VariableCode}:{value.Value:R}:{value.Unit}"));

    private ExperimentRunObservation BuildObservation(
        ResearchProject project,
        ExperimentRunPlan run,
        ExecutionComparisonRow execution,
        IReadOnlyList<InspectionRecord> records,
        ScenarioPackage? scenarioPackage)
    {
        var controlParameterValues = execution.ControlParameters
            .Select(static value => (value.Code, Value: ReadNumber(value.Value), value.Unit))
            .Where(static value => value.Value.HasValue)
            .ToDictionary(
                static value => value.Code,
                static value => new ActualValue(value.Value!.Value, value.Unit),
                StringComparer.Ordinal);
        var factors = new List<ExperimentFactorSetting>();
        var missing = new List<string>();
        foreach (var variable in project.Variables.Where(
                     static value => value.Role == ResearchVariableRoles.Control))
        {
            if (!TryResolveControlValue(variable, execution, controlParameterValues, out var value, out var reason))
            {
                missing.Add($"控制变量:{variable.Code}（{reason}）");
                continue;
            }
            factors.Add(new ExperimentFactorSetting
            {
                VariableCode = variable.Code,
                Value = value,
                Unit = variable.Unit
            });
        }
        var actualByCode = factors.ToDictionary(
            static value => value.VariableCode, static value => value.Value, StringComparer.Ordinal);
        var settingDeviation = run.Factors
            .Where(value => actualByCode.ContainsKey(value.VariableCode))
            .ToDictionary(
                static value => value.VariableCode,
                value => actualByCode[value.VariableCode] - value.Value,
                StringComparer.Ordinal);
        var hasSettingDeviation = run.Factors.Any(value =>
            !actualByCode.TryGetValue(value.VariableCode, out var actual) ||
            Math.Abs(actual - value.Value) > 1e-6 * Math.Max(1, Math.Abs(value.Value)));

        var outcomes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var objective in project.Objectives)
        {
            var sourceCode = ResolveInspectionCode(objective.Code, objective.DataSource);
            if (TryResolveMeasurement(records, sourceCode, objective.Unit, out var value, out var reason))
                outcomes[objective.Code] = value;
            else
                missing.Add($"目标:{objective.Code}（{reason}）");
        }
        var constraintOutcomes = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var constraint in project.OutcomeConstraints)
        {
            var sourceCode = ResolveInspectionCode(constraint.OutcomeCode, constraint.DataSource);
            if (TryResolveMeasurement(records, sourceCode, constraint.Unit, out var value, out var reason))
                constraintOutcomes[constraint.Code] = value;
            else
                missing.Add($"结果约束:{constraint.Code}（{reason}）");
        }

        var processFeatures = FlattenFeatures(execution);
        var context = new Dictionary<string, string>(execution.Context, StringComparer.Ordinal)
        {
            ["equipment_id"] = execution.EquipmentId
        };
        if (execution.EdgeIds.Count > 0)
            context["edge_ids"] = string.Join(',', execution.EdgeIds.Order(StringComparer.Ordinal));
        AddContext(context, "product_family_code", execution.ProductFamilyCode);
        AddContext(context, "product_code", execution.ProductCode);
        AddContext(context, "process_specification_id", execution.ProcessSpecificationId);
        AddContext(context, "process_specification_version", execution.ProcessSpecificationVersion);
        AddContext(context, "tooling_installation_id", execution.ToolingInstallationId);
        AddContext(context, "tooling_assembly_id", execution.ToolingAssemblyId);
        AddContext(context, "tooling_assembly_id", execution.ToolingAssemblyId);
        AddContext(context, "assembly_revision_id", execution.AssemblyRevisionId);
        AddContext(context, "assembly_revision", execution.AssemblyRevision);
        AddContext(context, "output_item_id", execution.OutputItemId);
        AddContext(context, "external_batch_ref", execution.ExternalBatchRef);
        AddContext(context, "material_lot_ref", execution.MaterialLotRef);
        if (scenarioPackage is not null)
        {
            context[ResearchContextAdmissionEvaluator.ObservationScenarioContextKey] =
                $"{scenarioPackage.PackageId}:{scenarioPackage.Version}";
            context[ResearchContextAdmissionEvaluator.ObservationPolicyHashContextKey] =
                ResearchContextAdmissionEvaluator.ComputePolicyHash(scenarioPackage);
        }
        if (execution.CompletedAt is null)
            missing.Add("过程执行未完成");
        if (execution.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable)
            missing.Add("过程数据不可用");
        if (processFeatures.Count == 0)
            missing.Add("没有可用过程特征");
        var contextAdmission = _contextAdmission.Evaluate(context, scenarioPackage);
        missing.AddRange(contextAdmission.ExclusionReasons);
        var valid = missing.Count == 0;
        var hashPayload = new
        {
            execution.ExecutionId,
            execution.CompletedAt,
            execution.AnalysisMaterialization.AlgorithmVersion,
            execution.AnalysisMaterialization.SourceMinIngestId,
            execution.AnalysisMaterialization.SourceMaxIngestId,
            execution.AnalysisMaterialization.SourceEventCount,
            execution.AnalysisMaterialization.SourceContentHash,
            Factors = factors,
            SettingDeviationFromPlan = settingDeviation,
            HasSettingDeviation = hasSettingDeviation,
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
            ExecutionKey = run.ExecutionKey,
            Context = context,
            ActualFactors = factors,
            SettingDeviationFromPlan = settingDeviation,
            HasSettingDeviation = hasSettingDeviation,
            ProcessFeatures = processFeatures,
            Outcomes = outcomes,
            ConstraintOutcomes = constraintOutcomes,
            ValidForOptimization = valid,
            ExclusionReason = valid ? null : string.Join("；", missing),
            SourceContentHash = contentHash
        };
    }

    private async Task<ScenarioPackage?> ResolveContextPolicyAsync(
        ResearchProject project,
        CancellationToken ct)
    {
        if (!ResearchContextAdmissionEvaluator.TryParseScenarioPackageReference(
                project.Context,
                out var packageId,
                out var version))
            return null;
        if (processConfigurations is null)
            throw new ProcessResearchRuleException("当前运行时无法解析研发项目引用的工艺配置。");
        var package = await processConfigurations.GetScenarioPackageAsync(packageId, version, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException($"研发项目引用的工艺配置不存在：{packageId} v{version}。");
        if (package.Status == ConfigurationStatuses.Draft)
            throw new ProcessResearchRuleException("研发项目不能使用仍可修改的草稿工艺配置进行正式分析。");
        var policyHash = ResearchContextAdmissionEvaluator.ComputePolicyHash(package);
        if (project.Context.TryGetValue(
                ResearchContextAdmissionEvaluator.PolicyHashContextKey,
                out var expectedHash) &&
            !string.Equals(expectedHash, policyHash, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("研发项目冻结的上下文策略哈希与工艺配置不一致。");
        return package;
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
        ExecutionComparisonRow execution,
        IReadOnlyDictionary<string, ActualValue> controlParameterValues,
        out double value,
        out string reason)
    {
        value = default;
        var source = variable.DataSource?.Trim();
        if (!string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("signal:", StringComparison.OrdinalIgnoreCase))
            return TryResolveSignalFeature(execution, source, variable.Unit, out value, out reason);
        if (!string.IsNullOrWhiteSpace(source) &&
            source.StartsWith("control-parameter:", StringComparison.OrdinalIgnoreCase))
        {
            var configuredProcessSpecificationCode = source["control-parameter:".Length..].Trim();
            return TryResolveProcessSpecificationValue(controlParameterValues, configuredProcessSpecificationCode, variable.Unit, out value, out reason);
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            reason = "数据来源必须是 control-parameter:<code> 或 signal:<code>:<feature>[:<phase>]";
            return false;
        }
        return TryResolveProcessSpecificationValue(controlParameterValues, variable.Code, variable.Unit, out value, out reason);
    }

    private static bool TryResolveProcessSpecificationValue(
        IReadOnlyDictionary<string, ActualValue> controlParameterValues,
        string code,
        string expectedUnit,
        out double value,
        out string reason)
    {
        value = default;
        if (!controlParameterValues.TryGetValue(code, out var actual) || !double.IsFinite(actual.Value))
        {
            reason = "缺少设备实际参数回读";
            return false;
        }
        if (!UnitsMatch(actual.Unit, expectedUnit))
        {
            reason = UnitConflict(expectedUnit, actual.Unit);
            return false;
        }
        value = actual.Value;
        reason = string.Empty;
        return true;
    }

    private static bool TryResolveSignalFeature(
        ExecutionComparisonRow execution,
        string source,
        string expectedUnit,
        out double value,
        out string reason)
    {
        value = default;
        var parts = source.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4)
        {
            reason = "过程信号来源格式无效";
            return false;
        }
        var signal = execution.Signals.FirstOrDefault(
            item => string.Equals(item.Code, parts[1], StringComparison.Ordinal));
        if (signal is null)
        {
            reason = "缺少实际过程信号";
            return false;
        }
        if (!UnitsMatch(signal.Unit, expectedUnit))
        {
            reason = UnitConflict(expectedUnit, signal.Unit);
            return false;
        }
        var matches = signal.Features.Where(feature =>
            string.Equals(feature.Code, parts[2], StringComparison.Ordinal) &&
            (parts.Length == 3
                ? feature.PhaseCode is null
                : string.Equals(feature.PhaseCode, parts[3], StringComparison.Ordinal)));
        var feature = matches
            .OrderBy(static item => item.PhaseOrder ?? 0)
            .FirstOrDefault(static item => item.Value.HasValue);
        if (feature?.Value is not { } resolved || !double.IsFinite(resolved))
        {
            reason = "缺少有效过程特征";
            return false;
        }
        value = resolved;
        reason = string.Empty;
        return true;
    }

    private static Dictionary<string, double> FlattenFeatures(ExecutionComparisonRow execution)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var signal in execution.Signals.OrderBy(static value => value.Code, StringComparer.Ordinal))
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
                    ? "execution"
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
        out double value,
        out string reason)
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
                    StringComparison.Ordinal));
        if (match.measurement?.NumericValue is not { } numeric)
        {
            reason = "缺少有效检验数值";
            return false;
        }
        if (!UnitsMatch(match.measurement.Unit, expectedUnit))
        {
            reason = UnitConflict(expectedUnit, match.measurement.Unit);
            return false;
        }
        value = decimal.ToDouble(numeric);
        if (!double.IsFinite(value))
        {
            reason = "检验数值不是有限数";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool UnitsMatch(string? actual, string expected)
        => !string.IsNullOrWhiteSpace(actual) &&
           !string.IsNullOrWhiteSpace(expected) &&
           string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string UnitConflict(string expected, string? actual)
        => string.IsNullOrWhiteSpace(actual)
            ? $"单位缺失，期望 {expected}"
            : $"单位冲突，期望 {expected}，实际 {actual.Trim()}";

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

    private sealed record ActualValue(double Value, string? Unit);
}
