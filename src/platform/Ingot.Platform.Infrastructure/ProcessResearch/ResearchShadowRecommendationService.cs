using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     Records shadow recommendations without dispatching them to equipment. A decision is
///     preregistered once; the actual outcome is attached once from acquisition and inspection
///     source data, so later knowledge cannot rewrite the engineer's original choice.
/// </summary>
public sealed class ResearchShadowRecommendationService(
    IProcessResearchStore store,
    IResearchObservationAssembler observationAssembler)
{
    public async Task<ResearchShadowRecommendation> RecordDecisionAsync(
        Guid experimentId,
        string suggestionRunKey,
        ResearchShadowDecisionRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("优化实验不存在。");
        var project = await store.GetProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        if (experiment.Optimization is null ||
            experiment.DesignMethod != ResearchDesignMethods.BayesianOptimization)
            throw new ProcessResearchRuleException("只有冻结了模型输入和预测的优化建议可以进入影子评估。");
        if (experiment.Optimization.Mode != ResearchOptimizationModes.Shadow)
            throw new ProcessResearchRuleException("只有显式生成的影子建议可以登记旁路决策。");

        suggestionRunKey = Required(request: suggestionRunKey, field: "模型建议运行标识", 120);
        var run = experiment.RunPlan.SingleOrDefault(value =>
            string.Equals(value.RunKey, suggestionRunKey, StringComparison.Ordinal))
            ?? throw new ProcessResearchRuleException("模型建议运行不存在。");
        var prediction = experiment.Optimization.RunPredictions.SingleOrDefault(value =>
            string.Equals(value.RunKey, suggestionRunKey, StringComparison.Ordinal))
            ?? throw new ProcessResearchRuleException("模型建议缺少冻结的预测快照。");
        if (await store.GetShadowRecommendationBySuggestionAsync(experimentId, suggestionRunKey, ct)
                .ConfigureAwait(false) is { } existing)
            return existing;

        var decision = Required(request.Decision, "影子决策", 40).ToLowerInvariant();
        if (!ResearchShadowDecisionStatuses.IsValid(decision))
            throw new ProcessResearchRuleException("影子决策必须是 accepted、modified 或 rejected。");
        var actualRunKey = Required(request.ActualRunKey, "工程师实际运行标识", 120);
        var selected = NormalizeSelectedFactors(project, request.EngineerSelectedFactors);
        var sameAsSuggestion = FactorsEqual(run.Factors, selected);
        if (decision == ResearchShadowDecisionStatuses.Accepted && !sameAsSuggestion)
            throw new ProcessResearchRuleException("接受建议时，工程师选择必须与冻结的模型建议一致。");
        if (decision != ResearchShadowDecisionStatuses.Accepted && sameAsSuggestion)
            throw new ProcessResearchRuleException("修改或拒绝建议时，必须登记不同的工程师实际选择。");
        var reason = Optional(request.RejectionReason, 2000);
        if (decision != ResearchShadowDecisionStatuses.Accepted && reason is null)
            throw new ProcessResearchRuleException("修改或拒绝模型建议时必须说明原因。");

        ValidateHardBoundaries(project, run.Factors, "模型建议");
        ValidateHardBoundaries(project, selected, "工程师选择");
        var limitations = request.SiteLimitations
            .Select(value => Required(value, "现场限制", 500))
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        if (request.SiteLimitations.Count > 50)
            throw new ProcessResearchRuleException("一条影子决策最多记录 50 条现场限制。");
        var context = NormalizeContext(project, request.ContextSnapshot);
        var historicalObservations = (await store.ListExperimentResultsAsync(project.ProjectId, ct)
                .ConfigureAwait(false))
            .SelectMany(static value => value.RunObservations)
            .Where(static value => value.ValidForOptimization)
            .ToArray();
        var applicability = AssessApplicability(
            project, run.Factors, context, historicalObservations);
        var now = DateTimeOffset.UtcNow;
        var snapshotHash = Hash(new
        {
            experiment.ExperimentId,
            experiment.ProjectRevision,
            suggestionRunKey,
            experiment.Optimization.ModelVersion,
            experiment.Optimization.InputHash,
            SuggestedFactors = run.Factors.OrderBy(static value => value.VariableCode),
            Prediction = prediction,
            decision,
            actualRunKey,
            EngineerSelectedFactors = selected.OrderBy(static value => value.VariableCode),
            reason,
            limitations,
            Context = context.OrderBy(static value => value.Key),
            applicability
        });
        var recommendation = new ResearchShadowRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            SuggestionRunKey = suggestionRunKey,
            ActualRunKey = actualRunKey,
            Decision = decision,
            ModelVersion = experiment.Optimization.ModelVersion,
            ModelInputHash = experiment.Optimization.InputHash,
            ProjectRevision = experiment.ProjectRevision,
            SuggestedFactors = run.Factors,
            EngineerSelectedFactors = selected,
            Prediction = prediction,
            Applicability = applicability,
            RejectionReason = reason,
            SiteLimitations = limitations,
            ContextSnapshot = context,
            DecisionSnapshotHash = snapshotHash,
            DecidedBy = Required(userId, "工程师", 240),
            DecidedAt = now
        };
        var saved = await store.CreateShadowRecommendationAsync(recommendation, ct)
            .ConfigureAwait(false);
        await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "shadow-recommendation",
            ResourceId = saved.RecommendationId.ToString(),
            Action = "decision-preregistered",
            ToStatus = decision,
            UserId = saved.DecidedBy,
            CreatedAt = now
        }, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchShadowCampaignReport> BuildReportAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var records = await store.ListShadowRecommendationsAsync(projectId, ct)
            .ConfigureAwait(false);
        var completed = records.Where(static value => value.Outcome is not null).ToArray();
        var calibration = project.Objectives.Select(objective =>
        {
            var checks = completed.Select(record =>
            {
                var hasPrediction = record.Prediction.Objectives.TryGetValue(
                    objective.Code, out var prediction);
                var hasOutcome = record.Outcome!.Outcomes.TryGetValue(
                    objective.Code, out var observed);
                return new { hasPrediction, hasOutcome, prediction, observed };
            }).Where(static value => value.hasPrediction && value.hasOutcome).ToArray();
            var covered = checks.Count(value =>
                value.observed >= value.prediction!.Lower95 &&
                value.observed <= value.prediction.Upper95);
            return new ResearchShadowCalibrationMetric
            {
                ObjectiveCode = objective.Code,
                CheckedCount = checks.Length,
                CoveredCount = covered,
                CoverageRate = checks.Length == 0 ? null : (double)covered / checks.Length
            };
        }).ToArray();
        var safetyEvents = completed.SelectMany(record =>
            project.OutcomeConstraints
                .Where(static constraint => constraint.SafetyCritical)
                .Where(constraint => record.Outcome!.ConstraintOutcomes.TryGetValue(
                    constraint.Code, out var value) && !ConstraintPassed(value, constraint.Operator, constraint.Limit))
                .Select(constraint => new ResearchShadowSafetyEvent
                {
                    RecommendationId = record.RecommendationId,
                    ActualRunKey = record.ActualRunKey,
                    ConstraintCode = constraint.Code,
                    ObservedValue = record.Outcome!.ConstraintOutcomes[constraint.Code],
                    Operator = constraint.Operator,
                    Limit = constraint.Limit,
                    Unit = constraint.Unit
                })).ToArray();
        var invalidCount = completed.Count(static value => !value.Outcome!.ValidForOptimization);
        var contextShiftCount = records.Count(static value =>
            value.Applicability.UnseenContextValues.Count > 0);
        var extrapolationCount = records.Count(static value =>
            value.Applicability.ParameterExtrapolations.Count > 0);
        var settingDeviationCount = completed.Count(record =>
            record.Outcome!.SettingDeviationFromEngineerSelection.Values.Any(static value =>
                Math.Abs(value) > 1e-6));
        var accepted = records.Count(static value =>
            value.Decision == ResearchShadowDecisionStatuses.Accepted);
        var modified = records.Count(static value =>
            value.Decision == ResearchShadowDecisionStatuses.Modified);
        var rejected = records.Count(static value =>
            value.Decision == ResearchShadowDecisionStatuses.Rejected);
        var signals = new List<ResearchShadowStopSignal>();
        if (safetyEvents.Length > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "safety-boundary-violation",
                Severity = "stop",
                Reason = $"发现 {safetyEvents.Length} 次实测安全结果约束违规。"
            });
        if (invalidCount >= 3)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "repeated-data-quality-failures",
                Severity = "stop",
                Reason = $"已有 {invalidCount} 条影子结果因数据不完整不可用于优化，应先修复数据链。"
            });
        var poorCalibration = calibration.Where(static value =>
            value.CheckedCount >= 5 && value.CoverageRate < 0.8).ToArray();
        if (poorCalibration.Length > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "prediction-interval-miscalibration",
                Severity = "stop",
                Reason = "至少一个目标的 95% 预测区间在不少于 5 次影子运行中覆盖率低于 80%。"
            });
        if (records.Count >= 5 && contextShiftCount * 2 > records.Count)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "context-domain-shift",
                Severity = "stop",
                Reason = "超过一半影子建议处于历史未覆盖的上下文组合，应重新分层或限定适用范围。"
            });
        if (records.Count >= 5 && rejected * 2 >= records.Count)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "high-engineer-rejection-rate",
                Severity = "warning",
                Reason = "工程师拒绝率达到 50%，应把拒绝原因转为新约束、映射修复或模型边界。"
            });
        if (extrapolationCount > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "parameter-extrapolation",
                Severity = "warning",
                Reason = $"有 {extrapolationCount} 条建议超出历史实际参数包络，但仍在项目硬边界内。"
            });
        var reasons = records
            .Where(static value => !string.IsNullOrWhiteSpace(value.RejectionReason))
            .Select(static value => value.RejectionReason!)
            .Concat(records.SelectMany(static value => value.SiteLimitations))
            .ToArray();
        var reportBody = new
        {
            ProjectId = projectId,
            Total = records.Count,
            accepted,
            modified,
            rejected,
            Completed = completed.Length,
            invalidCount,
            contextShiftCount,
            extrapolationCount,
            settingDeviationCount,
            calibration,
            safetyEvents,
            reasons,
            StopSignals = signals
        };
        return new ResearchShadowCampaignReport
        {
            ProjectId = projectId,
            TotalRecommendations = records.Count,
            AcceptedCount = accepted,
            ModifiedCount = modified,
            RejectedCount = rejected,
            AdoptionRate = records.Count == 0 ? null : (double)accepted / records.Count,
            CompletedOutcomeCount = completed.Length,
            InvalidOutcomeCount = invalidCount,
            ContextShiftCount = contextShiftCount,
            ParameterExtrapolationCount = extrapolationCount,
            SettingDeviationCount = settingDeviationCount,
            Calibration = calibration,
            SafetyEvents = safetyEvents,
            RejectionReasons = reasons,
            StopSignals = signals,
            StopRecommended = signals.Any(static value => value.Severity == "stop"),
            ReportHash = Hash(reportBody),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<ResearchShadowRecommendation> MaterializeOutcomeAsync(
        Guid recommendationId,
        string userId,
        CancellationToken ct = default)
    {
        var recommendation = await store.GetShadowRecommendationAsync(recommendationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("影子建议不存在。");
        if (recommendation.Outcome is not null)
            return recommendation;
        var project = await store.GetProjectAsync(recommendation.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var sourceExperiment = await store.GetExperimentAsync(recommendation.ExperimentId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("影子建议的模型快照不存在。");
        var actualRun = new ExperimentRunPlan
        {
            RunKey = recommendation.ActualRunKey,
            Sequence = 1,
            Factors = recommendation.EngineerSelectedFactors
        };
        var assembly = await observationAssembler.AssembleAsync(
            project,
            [sourceExperiment with
            {
                ExperimentId = Guid.CreateVersion7(),
                RunPlan = [actualRun],
                Status = ResearchExperimentStatuses.Planned,
                Optimization = null
            }],
            ct).ConfigureAwait(false);
        var observation = assembly.Observations.SingleOrDefault()
            ?? throw new ProcessResearchRuleException(
                "实际运行尚未形成可关联的完整生产周期，不能补齐影子结果。");
        var outcome = new ResearchShadowOutcome
        {
            ActualRunKey = recommendation.ActualRunKey,
            ActualFactors = observation.ActualFactors,
            SettingDeviationFromSuggestion = Differences(
                recommendation.SuggestedFactors, observation.ActualFactors),
            SettingDeviationFromEngineerSelection = Differences(
                recommendation.EngineerSelectedFactors, observation.ActualFactors),
            ProcessFeatures = observation.ProcessFeatures,
            Outcomes = observation.Outcomes,
            ConstraintOutcomes = observation.ConstraintOutcomes,
            ActualContextSnapshot = observation.Context,
            ValidForOptimization = observation.ValidForOptimization,
            ExclusionReason = observation.ExclusionReason,
            SourceContentHash = observation.SourceContentHash,
            CapturedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.AttachShadowOutcomeAsync(
            recommendation with { Outcome = outcome }, ct).ConfigureAwait(false);
        await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = recommendation.ProjectId,
            ResourceType = "shadow-recommendation",
            ResourceId = recommendation.RecommendationId.ToString(),
            Action = "source-outcome-frozen",
            FromStatus = recommendation.Decision,
            ToStatus = recommendation.Decision,
            UserId = Required(userId, "操作人", 240),
            CreatedAt = outcome.CapturedAt
        }, ct).ConfigureAwait(false);
        return saved;
    }

    private static IReadOnlyList<ExperimentFactorSetting> NormalizeSelectedFactors(
        ResearchProject project,
        IReadOnlyList<ExperimentFactorSetting> factors)
    {
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (!factors.Select(static value => value.VariableCode)
                .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys) ||
            factors.Count != controls.Count)
            throw new ProcessResearchRuleException("工程师选择必须包含且仅包含全部可控变量。");
        return factors.Select(value =>
        {
            if (!controls.TryGetValue(value.VariableCode, out var variable) ||
                !double.IsFinite(value.Value) ||
                variable.LowerLimit is { } lower && value.Value < lower ||
                variable.UpperLimit is { } upper && value.Value > upper)
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 超出项目边界。");
            if (!string.Equals(value.Unit?.Trim(), variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 的单位不一致。");
            return value with { Unit = variable.Unit };
        }).OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray();
    }

    private static ResearchShadowApplicabilityAssessment AssessApplicability(
        ResearchProject project,
        IReadOnlyList<ExperimentFactorSetting> suggested,
        IReadOnlyDictionary<string, string> context,
        IReadOnlyList<ExperimentRunObservation> history)
    {
        if (history.Count == 0)
            return new ResearchShadowApplicabilityAssessment
            {
                Status = ResearchApplicabilityStatuses.InsufficientHistory,
                Summary = "没有可用于判断参数包络和上下文覆盖的历史实测观察。"
            };
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var historyFactors = history
            .Where(value => value.ActualFactors.Select(static factor => factor.VariableCode)
                .ToHashSet(StringComparer.Ordinal).IsSupersetOf(controls.Keys))
            .ToArray();
        var extrapolations = new List<string>();
        foreach (var factor in suggested)
        {
            var values = historyFactors.SelectMany(value => value.ActualFactors)
                .Where(value => value.VariableCode == factor.VariableCode)
                .Select(static value => value.Value)
                .ToArray();
            if (values.Length > 0 && (factor.Value < values.Min() || factor.Value > values.Max()))
                extrapolations.Add($"{factor.VariableCode}:{factor.Value:R} not in [{values.Min():R},{values.Max():R}]");
        }
        double? nearestDistance = null;
        var suggestedByCode = suggested.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        foreach (var observation in historyFactors)
        {
            var observedByCode = observation.ActualFactors.ToDictionary(
                static value => value.VariableCode, static value => value.Value, StringComparer.Ordinal);
            var squared = controls.Sum(pair =>
            {
                var width = pair.Value.UpperLimit!.Value - pair.Value.LowerLimit!.Value;
                var delta = (suggestedByCode[pair.Key] - observedByCode[pair.Key]) / width;
                return delta * delta;
            });
            var distance = Math.Sqrt(squared);
            nearestDistance = nearestDistance is null ? distance : Math.Min(nearestDistance.Value, distance);
        }
        var ignoredContextKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "project_code", "project_revision", "feature_set_id", "feature_set_version"
        };
        var unseenContext = context
            .Where(pair => !ignoredContextKeys.Contains(pair.Key))
            .Where(pair =>
            {
                var historicalValues = history
                    .Where(value => value.Context.ContainsKey(pair.Key))
                    .Select(value => value.Context[pair.Key])
                    .ToHashSet(StringComparer.Ordinal);
                return historicalValues.Count > 0 && !historicalValues.Contains(pair.Value);
            })
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ToArray();
        var status = unseenContext.Length > 0
            ? ResearchApplicabilityStatuses.ContextShift
            : extrapolations.Count > 0
                ? ResearchApplicabilityStatuses.ParameterExtrapolation
                : ResearchApplicabilityStatuses.InDomain;
        var summary = status switch
        {
            ResearchApplicabilityStatuses.ContextShift =>
                $"发现 {unseenContext.Length} 个历史未出现的上下文取值。",
            ResearchApplicabilityStatuses.ParameterExtrapolation =>
                $"发现 {extrapolations.Count} 个超出历史实测包络的建议参数。",
            _ => "建议参数和已登记上下文均在历史实测覆盖内。"
        };
        return new ResearchShadowApplicabilityAssessment
        {
            Status = status,
            HistoricalObservationCount = history.Count,
            NearestNormalizedParameterDistance = nearestDistance,
            ParameterExtrapolations = extrapolations,
            UnseenContextValues = unseenContext,
            Summary = summary
        };
    }

    private static bool ConstraintPassed(double value, string op, double limit)
        => op switch
        {
            "<=" => value <= limit,
            ">=" => value >= limit,
            "<" => value < limit,
            ">" => value > limit,
            "==" => Math.Abs(value - limit) <= 1e-9,
            _ => false
        };

    private static IReadOnlyDictionary<string, string> NormalizeContext(
        ResearchProject project,
        IReadOnlyDictionary<string, string> supplied)
    {
        if (supplied.Count == 0)
            throw new ProcessResearchRuleException(
                "影子决策必须登记当时已知的设备、材料、工装或生产上下文，不能留下空快照。");
        if (supplied.Count > 100)
            throw new ProcessResearchRuleException("影子上下文最多包含 100 个字段。");
        var result = supplied
            .Select(pair => new KeyValuePair<string, string>(
                Required(pair.Key, "上下文键", 120).ToLowerInvariant(),
                Required(pair.Value, $"上下文 {pair.Key}", 500)))
            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Single().Value,
                StringComparer.Ordinal);
        result["project_code"] = project.Code;
        result["project_revision"] = project.Revision.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        result["feature_set_id"] = project.OptimizationFeatures.FeatureSetId;
        result["feature_set_version"] = project.OptimizationFeatures.Version.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    private static void ValidateHardBoundaries(
        ResearchProject project,
        IReadOnlyList<ExperimentFactorSetting> factors,
        string label)
    {
        var values = factors.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        foreach (var constraint in project.Constraints)
        {
            if (!values.TryGetValue(constraint.VariableCode, out var value))
                throw new ProcessResearchRuleException($"{label}缺少安全约束变量 {constraint.VariableCode}。");
            var passed = constraint.Operator switch
            {
                "<=" => value <= constraint.Limit,
                ">=" => value >= constraint.Limit,
                "<" => value < constraint.Limit,
                ">" => value > constraint.Limit,
                "==" => Math.Abs(value - constraint.Limit) <= 1e-9,
                _ => throw new ProcessResearchRuleException($"安全约束 {constraint.Code} 的操作符无效。")
            };
            if (!passed)
                throw new ProcessResearchRuleException($"{label}违反已声明安全边界 {constraint.Code}。");
        }
    }

    private static IReadOnlyDictionary<string, double> Differences(
        IReadOnlyList<ExperimentFactorSetting> expected,
        IReadOnlyList<ExperimentFactorSetting> actual)
    {
        var actualValues = actual.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        return expected
            .Where(value => actualValues.ContainsKey(value.VariableCode))
            .ToDictionary(static value => value.VariableCode,
                value => actualValues[value.VariableCode] - value.Value,
                StringComparer.Ordinal);
    }

    private static bool FactorsEqual(
        IReadOnlyList<ExperimentFactorSetting> left,
        IReadOnlyList<ExperimentFactorSetting> right)
    {
        if (left.Count != right.Count)
            return false;
        var rightByCode = right.ToDictionary(static value => value.VariableCode,
            StringComparer.Ordinal);
        return left.All(value => rightByCode.TryGetValue(value.VariableCode, out var other) &&
            string.Equals(value.Unit, other.Unit, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(value.Value - other.Value) <= Math.Max(1e-9, Math.Abs(value.Value) * 1e-9));
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static string Required(string? request, string field, int maximumLength)
    {
        var value = request?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且长度不能超过 {maximumLength}。 ");
        return value;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ProcessResearchRuleException($"说明长度不能超过 {maximumLength}。");
        return normalized;
    }
}
