using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     Computes the fail-closed gate for proposing or approving one controlled online run.
///     The gate is deliberately separate from device dispatch: passing it never grants Platform
///     permission to write a PLC or processSpecification system.
/// </summary>
public sealed class ResearchOnlineAdmissionService(
    IProcessResearchStore store,
    ResearchShadowRecommendationService shadowRecommendations,
    ResearchOnlineCampaignService onlineCampaign,
    IMechanismKnowledgeStore? mechanismKnowledgeStore = null) : IResearchOnlineAdmissionGate
{
    public const int MinimumValidShadowOutcomes = ValidationThresholds.MinimumCalibrationCheckCount;
    public const double MinimumPredictionCoverage = ValidationThresholds.MinimumCalibrationCoverage;

    public async Task<ResearchOnlineAdmissionEvidence> AssessAsync(
        Guid projectId,
        CancellationToken ct = default,
        string? requiredMechanismKnowledgeSnapshotHash = null)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        requiredMechanismKnowledgeSnapshotHash ??= mechanismKnowledgeStore is null
            ? "none"
            : MechanismKnowledgeExperimentPolicy.SnapshotHash(
                MechanismKnowledgeExperimentPolicy.Select(
                    project,
                    await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                    await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false)));
        var replayTask = store.ListHistoricalReplayReportsAsync(projectId, ct);
        var rollbackTask = store.ListRollbackDrillsAsync(projectId, ct);
        var experimentsTask = store.ListExperimentsAsync(projectId, ct);
        var resultsTask = store.ListExperimentResultsAsync(projectId, ct);
        var shadowTask = shadowRecommendations.BuildReportAsync(
            projectId, ct, requiredMechanismKnowledgeSnapshotHash);
        var onlineTask = onlineCampaign.BuildReportAsync(projectId, ct);
        await Task.WhenAll(replayTask, rollbackTask, experimentsTask, resultsTask, shadowTask, onlineTask)
            .ConfigureAwait(false);

        var reviewedReplay = (await replayTask.ConfigureAwait(false))
            .Where(static value =>
                value.Status == ResearchHistoricalReplayStatuses.Reviewed && value.GatePassed)
            .Where(value => string.Equals(
                value.MechanismKnowledgeSnapshotHash,
                requiredMechanismKnowledgeSnapshotHash,
                StringComparison.Ordinal))
            .OrderByDescending(static value => value.ReviewedAt ?? value.GeneratedAt)
            .FirstOrDefault();
        var shadow = await shadowTask.ConfigureAwait(false);
        var online = await onlineTask.ConfigureAwait(false);
        var rollbackDrill = (await rollbackTask.ConfigureAwait(false))
            .Where(static value =>
                value.Status == ResearchRollbackDrillStatuses.Reviewed && value.Passed)
            .Where(value => value.ProjectRevision == project.Revision)
            .OrderByDescending(static value => value.ReviewedAt ?? value.RecordedAt)
            .FirstOrDefault();
        var experiments = await experimentsTask.ConfigureAwait(false);
        var results = await resultsTask.ConfigureAwait(false);
        var failures = new List<string>();
        var warnings = new List<string>();

        if (project.Status is ResearchProjectStatuses.Draft or
            ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            failures.Add("项目必须处于 active 或 validating 状态。受控在线不能在草稿、已完成或已归档项目中运行。");
        if (reviewedReplay is null)
            failures.Add("缺少与当前机理知识快照一致、独立审核且通过门槛的历史回放报告。");
        if (rollbackDrill is null)
            failures.Add("缺少另一名工程师已复核且通过的停止与回退演练。");
        if (shadow.CompletedOutcomeCount - shadow.InvalidOutcomeCount < MinimumValidShadowOutcomes)
            failures.Add($"有效影子结果少于 {MinimumValidShadowOutcomes} 条，尚不足以校验在线建议。");
        if (shadow.StopRecommended)
            failures.Add("影子阶段存在停止信号，必须先处理安全、数据、校准或适用范围问题。");
        if (shadow.SafetyEvents.Count > 0)
            failures.Add("影子阶段存在实测安全结果约束违规。");
        if (online.StopRecommended)
            failures.Add("受控在线监控已触发停止信号，禁止生成下一条建议。");

        var calibrationByObjective = shadow.Calibration.ToDictionary(
            static value => value.ObjectiveCode, StringComparer.Ordinal);
        foreach (var objective in project.Objectives)
        {
            if (!calibrationByObjective.TryGetValue(objective.Code, out var calibration) ||
                calibration.CheckedCount < MinimumValidShadowOutcomes ||
                calibration.CoverageRate is null ||
                calibration.CoverageRate < MinimumPredictionCoverage)
            {
                var objectiveName = string.IsNullOrWhiteSpace(objective.Name) ? objective.Code : objective.Name;
                failures.Add(
                    $"目标“{objectiveName}”的影子预测区间校准未达到 " +
                    $"{MinimumValidShadowOutcomes} 次检查且覆盖率不低于 {MinimumPredictionCoverage:P0}。");
            }
        }

        var controlledIds = experiments
            .Where(static value => value.Optimization?.Mode == ResearchOptimizationModes.Controlled)
            .Select(static value => value.ExperimentId)
            .ToHashSet();
        if (results.Any(value => controlledIds.Contains(value.ExperimentId) && !value.SafetyPassed))
            failures.Add("既有受控在线结果发生安全约束违规，必须停止并完成人工复核和回退。");
        if (shadow.ContextShiftCount > 0)
            warnings.Add($"影子阶段有 {shadow.ContextShiftCount} 条建议处于未覆盖上下文；在线运行必须保持在已验证上下文内。");
        if (shadow.ParameterExtrapolationCount > 0)
            warnings.Add($"影子阶段有 {shadow.ParameterExtrapolationCount} 条参数外推；受控建议将被限制在历史实测参数包络内。");
        if (shadow.SettingDeviationCount > 0)
            warnings.Add($"影子阶段有 {shadow.SettingDeviationCount} 次实际设置偏差；在线结果必须继续记录建议值、批准值和实际值。");

        return new ResearchOnlineAdmissionEvidence
        {
            ValidationPolicyVersion = ValidationThresholds.PolicyVersion,
            MechanismKnowledgeSnapshotHash = requiredMechanismKnowledgeSnapshotHash,
            Eligible = failures.Count == 0,
            Failures = failures,
            Warnings = warnings,
            HistoricalReplayReportId = reviewedReplay?.ReportId,
            HistoricalReplayReportHash = reviewedReplay?.ReportHash,
            ShadowReportHash = shadow.ReportHash,
            RollbackDrillId = rollbackDrill?.DrillId,
            RollbackDrillRecordHash = rollbackDrill?.RecordHash,
            ValidShadowOutcomeCount = shadow.CompletedOutcomeCount - shadow.InvalidOutcomeCount,
            ShadowRecommendationCount = shadow.TotalRecommendations,
            AssessedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<ResearchOnlineAdmissionEvidence> RequireAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var evidence = await AssessAsync(projectId, ct).ConfigureAwait(false);
        if (!evidence.Eligible)
            throw new ProcessResearchRuleException(
                "受控在线准入未通过：" + string.Join("；", evidence.Failures));
        return evidence;
    }

    public async Task<ResearchOnlineAdmissionEvidence> RequireAsync(
        Guid projectId,
        string mechanismKnowledgeSnapshotHash,
        CancellationToken ct = default)
    {
        var evidence = await AssessAsync(projectId, ct, mechanismKnowledgeSnapshotHash)
            .ConfigureAwait(false);
        if (!evidence.Eligible)
            throw new ProcessResearchRuleException(
                "受控在线准入未通过：" + string.Join("；", evidence.Failures));
        return evidence;
    }
}
