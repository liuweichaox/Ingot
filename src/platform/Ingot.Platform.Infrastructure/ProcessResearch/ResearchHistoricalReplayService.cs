using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     Freezes a production-equivalent replay over real, already observed recipe conditions.
///     Replicates are aggregated before replay because the candidate-pool evaluator must never
///     pretend that the same recipe is a new optimization choice.
/// </summary>
public sealed class ResearchHistoricalReplayService(
    IProcessResearchStore store,
    IProcessOptimizerClient optimizerClient)
{
    public async Task<ResearchHistoricalReplayReport> RunAsync(
        Guid projectId,
        ResearchHistoricalReplayRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.SeedCount is < 1 or > 100)
            throw new ProcessResearchRuleException("历史回放随机种子数必须在 1 到 100 之间。");
        if (request.InitialObservationCount < 0)
            throw new ProcessResearchRuleException("历史回放初始观察数不能小于 0。");
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        var experiments = await store.ListExperimentsAsync(projectId, ct).ConfigureAwait(false);
        var results = await store.ListExperimentResultsAsync(projectId, ct).ConfigureAwait(false);
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var constraintCodes = project.OutcomeConstraints.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var runOrder = experiments
            .Where(static value => value.Optimization?.Mode != ResearchOptimizationModes.Shadow)
            .SelectMany(experiment => experiment.RunPlan.Select(run => new
            {
                run.RunKey,
                experiment.CreatedAt,
                run.Sequence
            }))
            .GroupBy(static value => value.RunKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(value => value.CreatedAt)
                    .ThenBy(value => value.Sequence).First(),
                StringComparer.Ordinal);
        var source = results
            .SelectMany(result => result.RunObservations.Select(observation => new
            {
                Observation = observation,
                ResultAt = result.RecordedAt
            }))
            .Where(value => value.Observation.ValidForOptimization &&
                value.Observation.ActualFactors.Select(static factor => factor.VariableCode)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys) &&
                value.Observation.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(objectiveCodes) &&
                value.Observation.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(constraintCodes))
            .OrderBy(value => runOrder.TryGetValue(value.Observation.RunKey, out var order)
                ? order.CreatedAt : value.ResultAt)
            .ThenBy(value => runOrder.TryGetValue(value.Observation.RunKey, out var order)
                ? order.Sequence : int.MaxValue)
            .ThenBy(value => value.Observation.RunKey, StringComparer.Ordinal)
            .ToArray();
        if (source.Length < 3)
            throw new ProcessResearchRuleException("真实历史回放至少需要 3 条参数、过程和结果完整的运行。");
        var commonFeatureKeys = source
            .Select(value => value.Observation.ProcessFeatures.Keys.ToHashSet(StringComparer.Ordinal))
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });
        var grouped = source
            .GroupBy(value => Signature(value.Observation.ActualFactors), StringComparer.Ordinal)
            .Select(group => new
            {
                FirstOrder = Array.IndexOf(source, group.First()),
                RunId = string.Join(',', group.Select(value => value.Observation.RunKey)
                    .Order(StringComparer.Ordinal)).Truncate(240),
                SourceCount = group.Count(),
                Params = controls.Keys.ToDictionary(
                    static code => code,
                    code => group.Average(item => item.Observation.ActualFactors
                        .Single(factor => factor.VariableCode == code).Value),
                    StringComparer.Ordinal),
                Outcomes = objectiveCodes.ToDictionary(
                    static code => code,
                    code => group.Average(item => item.Observation.Outcomes[code]),
                    StringComparer.Ordinal),
                ConstraintOutcomes = constraintCodes.ToDictionary(
                    static code => code,
                    code => group.Average(item => item.Observation.ConstraintOutcomes[code]),
                    StringComparer.Ordinal),
                ProcessFeatures = commonFeatureKeys.ToDictionary(
                    static code => code,
                    code => group.Average(item => item.Observation.ProcessFeatures[code]),
                    StringComparer.Ordinal)
            })
            .OrderBy(static value => value.FirstOrder)
            .ToArray();
        if (grouped.Length < 3)
            throw new ProcessResearchRuleException("历史运行至少需要 3 种不同的实际配方条件才能回放排序。");
        var budget = request.Budget ?? grouped.Length;
        if (budget < 1 || budget > grouped.Length)
            throw new ProcessResearchRuleException("历史回放预算必须在 1 和历史唯一条件数之间。");
        if (request.InitialObservationCount > budget)
            throw new ProcessResearchRuleException("初始观察数不能超过历史回放预算。");
        if (project.OutcomeConstraints.Count > 0 && request.InitialObservationCount == 0)
            throw new ProcessResearchRuleException("存在结果安全约束时，必须预注册至少一条初始观察。");
        var history = grouped.Select((value, index) =>
            new OptimizerHistoricalReplayObservationInput
            {
                Params = value.Params,
                Outcomes = value.Outcomes,
                ConstraintOutcomes = value.ConstraintOutcomes,
                ProcessFeatures = value.ProcessFeatures,
                RunId = value.RunId,
                OccurredAt = index
            }).ToArray();
        var call = new OptimizerHistoricalReplayCall
        {
            Campaign = ResearchExperimentOptimizer.BuildCampaign(
                project, ResearchOptimizationIntents.ReachSpecification, null),
            History = history,
            Budget = budget,
            SeedCount = request.SeedCount,
            InitialObservationCount = request.InitialObservationCount
        };
        var datasetHash = Hash(call);
        var raw = await optimizerClient.ReplayHistoryAsync(call, ct).ConfigureAwait(false);
        var optimizer = ReadSummary(raw, "optimizer");
        var random = ReadSummary(raw, "random");
        var originalTrials = ReadNullableInt(raw, "original_order_trials");
        var calibrationRows = raw.GetProperty("calibration").EnumerateArray().ToArray();
        var predictionChecks = calibrationRows.Sum(value =>
            value.GetProperty("prediction_interval_checks").GetInt32());
        var predictionCovered = calibrationRows.Sum(value =>
            value.GetProperty("prediction_interval_covered").GetInt32());
        var coverage = predictionChecks == 0
            ? null
            : (double?)predictionCovered / predictionChecks;
        var optimizerSafetyViolations = raw.GetProperty("safety_violations")
            .GetProperty("optimizer").EnumerateArray().Sum(static value => value.GetInt32());
        var enginePolicy = raw.GetProperty("engine_policy").GetString() ?? "";
        var evidenceKind = raw.GetProperty("evidence_kind").GetString() ?? "";
        var limitations = raw.GetProperty("limitations").GetString() ?? "";
        var gateFailures = new List<string>();
        if (!enginePolicy.StartsWith("production-equivalent:", StringComparison.Ordinal))
            gateFailures.Add("回放没有声明生产等价模型切换路径。");
        if (!AuditNoFutureLeakage(raw))
            gateFailures.Add("逐步轨迹检测到未来信息泄漏或审计字段不完整。");
        if (grouped.Length < 5)
            gateFailures.Add("唯一历史条件少于 5 个，只能形成探索性回放证据。");
        if (optimizerSafetyViolations > 0)
            gateFailures.Add($"优化器回放累计触发 {optimizerSafetyViolations} 次安全结果约束违规。");
        if (predictionChecks == 0 ||
            coverage < ValidationThresholds.MinimumCalibrationCoverage)
            gateFailures.Add("预测区间没有可校准检查，或聚合覆盖率低于预注册的 80% 最低门槛。");
        if (optimizer.SuccessRate < random.SuccessRate)
            gateFailures.Add("优化器达到规格的成功率低于随机候选顺序。");
        if (originalTrials is not null && optimizer.MedianTrials is not null &&
            optimizer.MedianTrials > originalTrials)
            gateFailures.Add("优化器达到规格的中位试验数劣于历史工程师原顺序。");
        var reportHash = Hash(new
        {
            datasetHash,
            Raw = raw,
            ValidationThresholds.PolicyVersion
        });
        var report = new ResearchHistoricalReplayReport
        {
            ReportId = Guid.CreateVersion7(),
            ProjectId = projectId,
            ValidationPolicyVersion = ValidationThresholds.PolicyVersion,
            DatasetSnapshotHash = datasetHash,
            UniqueConditionCount = grouped.Length,
            SourceRunCount = grouped.Sum(static value => value.SourceCount),
            Budget = budget,
            SeedCount = request.SeedCount,
            InitialObservationCount = request.InitialObservationCount,
            OriginalOrderTrials = originalTrials,
            Optimizer = optimizer,
            Random = random,
            PredictionIntervalCoverage = coverage,
            PredictionIntervalChecks = predictionChecks,
            OptimizerSafetyViolationCount = optimizerSafetyViolations,
            EnginePolicy = enginePolicy,
            EvidenceKind = evidenceKind,
            Limitations = limitations,
            GatePassed = gateFailures.Count == 0,
            GateFailures = gateFailures,
            RawResult = raw,
            ReportHash = reportHash,
            GeneratedBy = RequiredUser(userId),
            GeneratedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.CreateHistoricalReplayReportAsync(report, ct)
            .ConfigureAwait(false);
        await AuditAsync(saved, "generated", saved.GeneratedBy, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchHistoricalReplayReport> ReviewAsync(
        Guid reportId,
        string userId,
        CancellationToken ct = default)
    {
        var report = await store.GetHistoricalReplayReportAsync(reportId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("历史回放报告不存在。");
        if (report.Status == ResearchHistoricalReplayStatuses.Reviewed)
            return report;
        var actor = RequiredUser(userId);
        if (string.Equals(report.GeneratedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("历史回放报告生成者和审核者必须分离。");
        var reviewed = report with
        {
            Status = ResearchHistoricalReplayStatuses.Reviewed,
            ReviewedBy = actor,
            ReviewedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.ReviewHistoricalReplayReportAsync(reviewed, ct)
            .ConfigureAwait(false);
        await AuditAsync(saved, "reviewed", actor, ct).ConfigureAwait(false);
        return saved;
    }

    private async Task AuditAsync(
        ResearchHistoricalReplayReport report,
        string action,
        string userId,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = report.ProjectId,
            ResourceType = "historical-replay-report",
            ResourceId = report.ReportId.ToString(),
            Action = action,
            ToStatus = report.Status,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

    private static ResearchReplayMethodSummary ReadSummary(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        return new ResearchReplayMethodSummary
        {
            SuccessRate = value.GetProperty("success_rate").GetDouble(),
            MedianTrials = ReadNullableDouble(value, "median_trials"),
            MeanTrials = ReadNullableDouble(value, "mean_trials"),
            Runs = value.GetProperty("runs").GetInt32()
        };
    }

    private static bool AuditNoFutureLeakage(JsonElement root)
    {
        foreach (var trace in root.GetProperty("step_traces").EnumerateArray())
        {
            foreach (var step in trace.EnumerateArray())
            {
                if (!step.TryGetProperty("revealed_history_index", out var revealed) ||
                    !step.TryGetProperty("visible_observation_indices_before", out var visible))
                    return false;
                if (visible.EnumerateArray().Any(value => value.GetInt32() == revealed.GetInt32()))
                    return false;
            }
        }
        return true;
    }

    private static int? ReadNullableInt(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private static double? ReadNullableDouble(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
    }

    private static string Signature(IReadOnlyList<ExperimentFactorSetting> factors)
        => string.Join('|', factors.OrderBy(static value => value.VariableCode, StringComparer.Ordinal)
            .Select(static value => $"{value.VariableCode}:{value.Value:R}:{value.Unit}"));

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static string RequiredUser(string? value)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length > 240)
            throw new ProcessResearchRuleException("操作人无效。");
        return result;
    }
}

internal static class ResearchReplayStringExtensions
{
    public static string Truncate(this string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
