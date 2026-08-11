using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class ProcessResearchWorkflow
{
    internal async Task<ResearchExperimentResult> RecordMaterializedExperimentResultAsync(
        Guid experimentId,
        ResearchExperimentResult request,
        string userId,
        CancellationToken ct = default)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实验不存在。");
        var project = await RequireMutableProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false);
        if (experiment.Status != ResearchExperimentStatuses.Running)
            throw new ProcessResearchRuleException("只有执行中的实验可以记录结果。");
        if (request.Metrics.Count == 0)
            throw new ProcessResearchRuleException("实验结果必须包含目标指标的计算结果。");
        if (!request.CalculatedFromSource)
            throw new ProcessResearchRuleException("实验结果必须由不可变的数据快照计算，不能手工填报结论。");
        var objectives = project.Objectives.ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var metrics = request.Metrics.Select(metric =>
        {
            var code = NormalizeCode(metric.ObjectiveCode, "结果指标");
            if (!experiment.ObjectiveCodes.Contains(code, StringComparer.Ordinal) ||
                !objectives.TryGetValue(code, out var objective))
                throw new ProcessResearchRuleException($"结果指标 {code} 不属于当前实验目标。");
            var hasLowerBound = metric.LowerConfidenceBound is not null;
            var hasUpperBound = metric.UpperConfidenceBound is not null;
            var effectTolerance = Math.Max(
                1e-9,
                1e-9 * Math.Max(Math.Abs(metric.ObservedValue), Math.Abs(metric.BaselineValue)));
            if (!double.IsFinite(metric.BaselineValue) || !double.IsFinite(metric.ObservedValue) ||
                !double.IsFinite(metric.EffectValue) ||
                metric.LowerConfidenceBound is { } lower && !double.IsFinite(lower) ||
                metric.UpperConfidenceBound is { } upper && !double.IsFinite(upper) ||
                metric.LowerConfidenceBound is { } min &&
                metric.UpperConfidenceBound is { } max && min > max ||
                hasLowerBound != hasUpperBound ||
                hasLowerBound &&
                (metric.BaselineSampleCount < 2 || metric.ExperimentSampleCount < 2) ||
                metric.BaselineSampleCount < 0 || metric.ExperimentSampleCount < 1 ||
                Math.Abs(metric.EffectValue -
                         (metric.ObservedValue - metric.BaselineValue)) > effectTolerance)
                throw new ProcessResearchRuleException($"结果指标 {code} 的数值或样本量无效。");
            var unit = RequiredText(metric.Unit, "结果指标单位", 40);
            if (!string.Equals(unit, objective.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"结果指标 {code} 的单位必须与研发目标一致。");
            return metric with
            {
                ObjectiveCode = code,
                Unit = unit,
                ComputationMethod = RequiredText(metric.ComputationMethod, "计算方法", 240)
            };
        }).ToArray();
        if (metrics.Select(static value => value.ObjectiveCode).Distinct(StringComparer.Ordinal).Count() !=
            metrics.Length)
            throw new ProcessResearchRuleException("同一实验结果中的目标指标不能重复。");
        if (experiment.ObjectiveCodes.Any(code =>
                metrics.All(metric => metric.ObjectiveCode != code)))
            throw new ProcessResearchRuleException("单次结果记录必须覆盖实验的全部目标。");

        var controlVariables = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var runObservations = request.RunObservations.Select(observation =>
        {
            var factors = observation.ActualFactors.Select(factor =>
            {
                var code = NormalizeCode(factor.VariableCode, "实际工艺变量");
                if (!controlVariables.TryGetValue(code, out var variable) ||
                    !double.IsFinite(factor.Value) ||
                    !string.Equals(factor.Unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                    throw new ProcessResearchRuleException($"运行观察中的工艺变量 {code} 无效。");
                return factor with { VariableCode = code, Unit = variable.Unit };
            }).ToArray();
            if (!factors.Select(static value => value.VariableCode)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(controlVariables.Keys))
                throw new ProcessResearchRuleException("每条运行观察必须包含全部可控工艺变量。");
            if (!observation.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(experiment.ObjectiveCodes) ||
                !observation.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(project.OutcomeConstraints.Select(static value => value.Code)) ||
                observation.Outcomes.Values.Any(static value => !double.IsFinite(value)) ||
                observation.ConstraintOutcomes.Values.Any(static value => !double.IsFinite(value)) ||
                observation.ProcessFeatures.Values.Any(static value => !double.IsFinite(value)))
                throw new ProcessResearchRuleException(
                    "运行观察必须完整包含实验目标、结果约束且所有特征均为有限数值。");
            var sourceHash = observation.SourceContentHash.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(sourceHash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new ProcessResearchRuleException("运行观察来源摘要必须是 64 位 SHA-256。");
            if (!observation.ValidForOptimization && string.IsNullOrWhiteSpace(observation.ExclusionReason))
                throw new ProcessResearchRuleException("排除运行观察时必须说明原因。");
            return observation with
            {
                ExecutionKey = RequiredText(observation.ExecutionKey, "运行观察标识", 240),
                ActualFactors = factors,
                SourceContentHash = sourceHash,
                ExclusionReason = OptionalText(observation.ExclusionReason, 1000)
            };
        }).ToArray();
        if (runObservations.Select(static value => value.ExecutionKey)
                .Distinct(StringComparer.Ordinal).Count() != runObservations.Length)
            throw new ProcessResearchRuleException("同一结果中的运行观察标识不能重复。");
        if (!runObservations.Select(static value => value.ExecutionKey)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(experiment.RunPlan.Select(static value => value.ExecutionKey)))
            throw new ProcessResearchRuleException("实验结果必须包含计划中每个 ExecutionKey 的逐运行源数据观察。");

        var replicateCount = experiment.RunPlan
            .Where(static value => !string.IsNullOrWhiteSpace(value.ReplicateKey))
            .GroupBy(static value => value.ReplicateKey!, StringComparer.Ordinal)
            .Select(static group => group.Count())
            .DefaultIfEmpty(1)
            .Min();
        var distinctBlockCount = Math.Max(
            1,
            experiment.RunPlan
                .Select(static value => value.BlockKey)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Count());
        var distinctMaterialLotCount = CountDistinctContext(
            runObservations,
            "material_lot",
            "material_lot_id",
            "material_batch",
            "batch_id");
        var distinctEquipmentCount = CountDistinctContext(runObservations, "equipment_id");
        var safetyPassed = project.OutcomeConstraints.All(constraint =>
            runObservations.All(observation =>
                observation.ConstraintOutcomes.TryGetValue(constraint.Code, out var outcome) &&
                (constraint.Operator == "<="
                    ? outcome <= constraint.Limit
                    : outcome >= constraint.Limit)));
        var excludedExecutionKeys = runObservations
            .Where(static value => !value.ValidForOptimization)
            .Select(static value => value.ExecutionKey)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var resultId = request.ResultId == Guid.Empty ? Guid.CreateVersion7() : request.ResultId;
        if (await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false) is not null)
            throw new ProcessResearchRuleException("实验结果标识已经存在。");
        var analysisRunId = request.AnalysisRunId == Guid.Empty
            ? Guid.CreateVersion7()
            : request.AnalysisRunId;
        var datasetSnapshotId = RequiredText(request.DatasetSnapshotId, "数据快照", 500);
        var now = DateTimeOffset.UtcNow;
        var hashPayload = JsonSerializer.Serialize(new
        {
            experiment.ProjectId,
            experimentId,
            resultId,
            datasetSnapshotId,
            analysisRunId,
            metrics,
            runObservations,
            RunCount = runObservations.Length,
            ReplicateCount = replicateCount,
            DistinctBlockCount = distinctBlockCount,
            DistinctMaterialLotCount = distinctMaterialLotCount,
            DistinctEquipmentCount = distinctEquipmentCount,
            SafetyPassed = safetyPassed,
            ExcludedExecutionKeys = excludedExecutionKeys
        });
        var analysisHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload)));
        var evidence = NormalizeEvidence(experiment.ProjectId, request.Evidence).ToList();
        evidence.Add(CreateEvidence(
            experiment.ProjectId,
            EvidenceKinds.DatasetSnapshot,
            datasetSnapshotId,
            "实验结果使用的数据快照。",
            Sha256(datasetSnapshotId),
            now));
        evidence.Add(CreateEvidence(
            experiment.ProjectId,
            EvidenceKinds.AnalysisRun,
            analysisRunId.ToString(),
            "由实验数据计算得到的分析运行。",
            analysisHash,
            now));

        var value = request with
        {
            ResultId = resultId,
            ProjectId = experiment.ProjectId,
            ExperimentId = experimentId,
            DatasetSnapshotId = datasetSnapshotId,
            AnalysisRunId = analysisRunId,
            AnalysisHash = analysisHash,
            Metrics = metrics,
            RunObservations = runObservations,
            RunCount = runObservations.Length,
            ReplicateCount = replicateCount,
            DistinctBlockCount = distinctBlockCount,
            DistinctMaterialLotCount = distinctMaterialLotCount,
            DistinctEquipmentCount = distinctEquipmentCount,
            SafetyPassed = safetyPassed,
            Evidence = evidence,
            ExcludedExecutionKeys = excludedExecutionKeys,
            RecordedBy = NormalizeUser(userId),
            RecordedAt = now
        };
        var updatedExperiment = experiment with
        {
            ResultIds = experiment.ResultIds.Append(resultId).Distinct().ToArray(),
            Status = ResearchExperimentStatuses.Completed,
            Execution = (experiment.Execution ?? BuildExecution(experiment)) with
            {
                State = ResearchExperimentExecutionStates.Completed,
                CompletedAt = now
            },
            UpdatedAt = now
        };
        var savedResult = await store.SaveExperimentResultTransactionAsync(
            value,
            updatedExperiment,
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = experiment.ProjectId,
                ResourceType = "experiment-result",
                ResourceId = resultId.ToString(),
                Action = "recorded",
                FromStatus = null,
                ToStatus = safetyPassed ? "passed" : "failed",
                UserId = NormalizeUser(userId),
                CreatedAt = now
            },
            ct).ConfigureAwait(false);
        await UpdateHypothesisAfterResultAsync(experiment, savedResult, userId, ct)
            .ConfigureAwait(false);
        return savedResult;
    }
}
