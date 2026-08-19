using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
///     Deterministic monitoring report for controlled online runs. It compares prediction
///     residuals with the preceding shadow campaign without claiming causality. A systematic
///     shift is signalled only after both groups contain at least five measured outcomes and
///     the approximate 95% interval for the residual-mean difference excludes zero.
/// </summary>
public sealed class ResearchOnlineCampaignService(IProcessResearchStore store)
{
    private const int MinimumComparisonCount = 5;

    public async Task<ResearchOnlineCampaignReport> BuildReportAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var projectTask = store.GetProjectAsync(projectId, ct);
        var experimentsTask = store.ListExperimentsAsync(projectId, ct);
        var resultsTask = store.ListExperimentResultsAsync(projectId, ct);
        var shadowTask = store.ListShadowRecommendationsAsync(projectId, ct);
        await Task.WhenAll(projectTask, experimentsTask, resultsTask, shadowTask)
            .ConfigureAwait(false);
        var project = await projectTask.ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var controlled = (await experimentsTask.ConfigureAwait(false))
            .Where(static value => value.Optimization?.Mode == ResearchOptimizationModes.Controlled)
            .OrderBy(static value => value.CreatedAt).ToArray();
        var controlledIds = controlled.Select(static value => value.ExperimentId).ToHashSet();
        var results = (await resultsTask.ConfigureAwait(false))
            .Where(value => controlledIds.Contains(value.ExperimentId)).ToArray();
        var experimentsById = controlled.ToDictionary(static value => value.ExperimentId);
        var onlinePairs = results.SelectMany(result =>
        {
            var experiment = experimentsById[result.ExperimentId];
            var predictions = experiment.Optimization!.RunPredictions
                .ToDictionary(static value => value.ExecutionKey, StringComparer.Ordinal);
            return result.RunObservations
                .Where(static value => value.ValidForOptimization)
                .Where(value => predictions.ContainsKey(value.ExecutionKey))
                .Select(value => new PredictionOutcomePair(predictions[value.ExecutionKey], value));
        }).ToArray();
        var shadowPairs = (await shadowTask.ConfigureAwait(false))
            .Where(static value => value.Outcome is { ValidForOptimization: true })
            .Select(static value => new PredictionOutcomePair(
                value.Prediction,
                new ExperimentRunObservation
                {
                    ExecutionKey = value.ActualExecutionKey,
                    Outcomes = value.Outcome!.Outcomes,
                    SourceContentHash = value.Outcome.SourceContentHash
                }))
            .ToArray();

        var calibration = project.Objectives.Select(objective =>
        {
            var checks = onlinePairs.Where(pair =>
                pair.Prediction.Objectives.ContainsKey(objective.Code) &&
                pair.Observation.Outcomes.ContainsKey(objective.Code)).ToArray();
            var covered = checks.Count(pair =>
            {
                var prediction = pair.Prediction.Objectives[objective.Code];
                var observed = pair.Observation.Outcomes[objective.Code];
                return observed >= prediction.Lower95 && observed <= prediction.Upper95;
            });
            return new ResearchShadowCalibrationMetric
            {
                ObjectiveCode = objective.Code,
                CheckedCount = checks.Length,
                CoveredCount = covered,
                CoverageRate = checks.Length == 0 ? null : (double)covered / checks.Length
            };
        }).ToArray();
        var comparisons = project.Objectives.Select(objective =>
        {
            var shadowResiduals = Residuals(shadowPairs, objective.Code);
            var onlineResiduals = Residuals(onlinePairs, objective.Code);
            double? shift = null;
            double? lower = null;
            double? upper = null;
            var systematic = false;
            if (shadowResiduals.Length > 0 && onlineResiduals.Length > 0)
            {
                shift = onlineResiduals.Average() - shadowResiduals.Average();
                var standardError = Math.Sqrt(
                    SampleVariance(onlineResiduals) / onlineResiduals.Length +
                    SampleVariance(shadowResiduals) / shadowResiduals.Length);
                lower = shift - 1.96 * standardError;
                upper = shift + 1.96 * standardError;
                systematic = shadowResiduals.Length >= MinimumComparisonCount &&
                    onlineResiduals.Length >= MinimumComparisonCount &&
                    (lower > 0 || upper < 0);
            }
            return new ResearchOnlineResidualComparison
            {
                ObjectiveCode = objective.Code,
                ShadowCount = shadowResiduals.Length,
                OnlineCount = onlineResiduals.Length,
                ShadowMeanResidual = shadowResiduals.Length == 0 ? null : shadowResiduals.Average(),
                OnlineMeanResidual = onlineResiduals.Length == 0 ? null : onlineResiduals.Average(),
                MeanResidualShift = shift,
                ShiftLower95 = lower,
                ShiftUpper95 = upper,
                SystematicShiftDetected = systematic
            };
        }).ToArray();

        var signals = new List<ResearchShadowStopSignal>();
        var safetyViolations = results.Count(static value => !value.SafetyPassed);
        if (safetyViolations > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "controlled-safety-violation",
                Severity = "stop",
                Reason = $"受控在线结果中发现 {safetyViolations} 次安全约束违规。"
            });
        var shifted = comparisons.Where(static value => value.SystematicShiftDetected).ToArray();
        if (shifted.Length > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "shadow-online-systematic-shift",
                Severity = "stop",
                Reason = "在线实测与影子阶段预测残差出现统计上可辨别的系统性偏移，必须先解释或重新限定适用范围。"
            });
        var invalidCount = results.SelectMany(static value => value.RunObservations)
            .Count(static value => !value.ValidForOptimization);
        if (invalidCount >= 3)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "controlled-repeated-data-failure",
                Severity = "stop",
                Reason = $"受控在线已有 {invalidCount} 条结果因数据不完整失效。"
            });
        var settingDeviationCount = onlinePairs.Count(static value => value.Observation.HasSettingDeviation);
        if (settingDeviationCount > 0)
            signals.Add(new ResearchShadowStopSignal
            {
                Code = "controlled-setting-deviation",
                Severity = "warning",
                Reason = $"受控在线发现 {settingDeviationCount} 次批准设置与实际回读偏差。"
            });
        var body = new
        {
            ProjectId = projectId,
            Controlled = controlled.Select(static value => new
            {
                value.ExperimentId,
                value.Status,
                value.ControlledDecision?.Decision,
                value.Optimization!.InputHash
            }),
            ResultHashes = results.SelectMany(static value => value.RunObservations)
                .Select(static value => value.SourceContentHash).Order(StringComparer.Ordinal),
            calibration,
            comparisons,
            signals
        };
        return new ResearchOnlineCampaignReport
        {
            ProjectId = projectId,
            TotalSuggestions = controlled.Length,
            AcceptedCount = controlled.Count(static value =>
                value.ControlledDecision?.Decision == ResearchControlledDecisionStatuses.Accepted),
            ModifiedCount = controlled.Count(static value =>
                value.ControlledDecision?.Decision == ResearchControlledDecisionStatuses.Modified),
            RejectedCount = controlled.Count(static value =>
                value.ControlledDecision?.Decision == ResearchControlledDecisionStatuses.Rejected),
            RunningCount = controlled.Count(static value => value.Status == ResearchExperimentStatuses.Running),
            CompletedResultCount = results.Length,
            ValidOutcomeCount = onlinePairs.Length,
            SettingDeviationCount = settingDeviationCount,
            SafetyViolationCount = safetyViolations,
            Calibration = calibration,
            ShadowComparisons = comparisons,
            StopSignals = signals,
            StopRecommended = signals.Any(static value => value.Severity == "stop"),
            ReportHash = Hash(body),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static double[] Residuals(
        IReadOnlyList<PredictionOutcomePair> pairs,
        string objectiveCode)
        => pairs.Where(pair =>
                pair.Prediction.Objectives.ContainsKey(objectiveCode) &&
                pair.Observation.Outcomes.ContainsKey(objectiveCode))
            .Select(pair => pair.Observation.Outcomes[objectiveCode] -
                pair.Prediction.Objectives[objectiveCode].Mean)
            .Where(double.IsFinite)
            .ToArray();

    private static double SampleVariance(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0;
        var mean = values.Average();
        return values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1);
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private sealed record PredictionOutcomePair(
        OptimizationRunPrediction Prediction,
        ExperimentRunObservation Observation);
}
