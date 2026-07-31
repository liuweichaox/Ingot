using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class ProcessResearchWorkflow(IProcessResearchStore store)
{
    public async Task<ResearchProject> CreateProjectAsync(
        ResearchProject draft,
        string userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var value = NormalizeProject(
            draft with
            {
                ProjectId = draft.ProjectId == Guid.Empty ? Guid.CreateVersion7() : draft.ProjectId,
                Status = ResearchProjectStatuses.Draft,
                OwnerUserId = NormalizeUser(userId),
                CreatedAt = now,
                UpdatedAt = now,
                Revision = 1
            });

        if (await store.GetProjectAsync(value.ProjectId, ct).ConfigureAwait(false) is not null)
            throw new ProcessResearchRuleException("研发项目标识已经存在。");
        if (await store.GetProjectByCodeAsync(value.Code, ct).ConfigureAwait(false) is not null)
            throw new ProcessResearchRuleException("研发项目代码已经存在。");

        var saved = await store.SaveProjectAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(saved.ProjectId, "project", saved.ProjectId.ToString(), "created",
            userId, null, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProject> UpdateProjectAsync(
        Guid projectId,
        ResearchProject request,
        string userId,
        CancellationToken ct = default)
    {
        _ = NormalizeUser(userId);
        var existing = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        if (request.Revision != existing.Revision)
            throw new ProcessResearchRuleException("研发项目已被其他人修改，请刷新后重试。");
        if (existing.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");

        var value = NormalizeProject(
            request with
            {
                ProjectId = existing.ProjectId,
                Code = existing.Code,
                Status = existing.Status,
                OwnerUserId = existing.OwnerUserId,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = existing.Revision + 1
            });
        var saved = await store.SaveProjectAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(projectId, "project", projectId.ToString(), "updated",
            userId, existing.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProject> ChangeProjectStatusAsync(
        Guid projectId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        _ = NormalizeUser(userId);
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        targetStatus = NormalizeStatus(targetStatus, ResearchProjectStatuses.IsValid, "研发项目状态");
        var allowed = (project.Status, targetStatus) switch
        {
            (ResearchProjectStatuses.Draft, ResearchProjectStatuses.Active) => true,
            (ResearchProjectStatuses.Active, ResearchProjectStatuses.Validating) => true,
            (ResearchProjectStatuses.Validating, ResearchProjectStatuses.Active) => true,
            (ResearchProjectStatuses.Validating, ResearchProjectStatuses.Completed) => true,
            (_, ResearchProjectStatuses.Archived)
                when project.Status != ResearchProjectStatuses.Archived => true,
            _ => false
        };
        if (!allowed)
            throw new ProcessResearchRuleException(
                $"研发项目状态不能从 {project.Status} 转换为 {targetStatus}。");
        if (targetStatus == ResearchProjectStatuses.Active &&
            (project.Objectives.Count == 0 || project.Variables.Count == 0))
            throw new ProcessResearchRuleException("研发项目进入执行阶段前必须定义目标和变量。");
        if (targetStatus == ResearchProjectStatuses.Completed)
        {
            var windows = await store.ListProcessWindowsAsync(projectId, ct).ConfigureAwait(false);
            if (windows.All(static value =>
                    value.Status != ProcessWindowStatuses.Validated ||
                    value.ValidationLevel is not (
                        ProcessWindowValidationLevels.Laboratory or
                        ProcessWindowValidationLevels.Production)))
                throw new ProcessResearchRuleException(
                    "研发项目完成前必须形成经过跨区组重复实验验证的工艺窗口。");
        }

        var saved = await store.SaveProjectAsync(
            project with
            {
                Status = targetStatus,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = project.Revision + 1
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "project", projectId.ToString(), "status-changed",
            userId, project.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchHypothesis> SaveHypothesisAsync(
        Guid projectId,
        ResearchHypothesis request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var existing = request.HypothesisId == Guid.Empty
            ? null
            : await store.GetHypothesisAsync(request.HypothesisId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("研发假设不属于当前项目。");

        var statement = RequiredText(request.Statement, "研发假设", 4000);
        var rationale = RequiredText(request.Rationale, "假设依据", 8000);
        var variableCodes = NormalizeCodes(request.VariableCodes, "假设变量");
        var knownVariables = project.Variables.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (variableCodes.Any(code => !knownVariables.Contains(code)))
            throw new ProcessResearchRuleException("研发假设引用了项目中未定义的变量。");
        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var validationOutcomeCode = request.ValidationOutcomeCode is null
            ? null
            : NormalizeCode(request.ValidationOutcomeCode, "假设验证目标");
        var expectedEffectDirection = request.ExpectedEffectDirection is null
            ? null
            : RequiredText(request.ExpectedEffectDirection, "预期效应方向", 40)
                .ToLowerInvariant();
        var hasValidationCriterion = validationOutcomeCode is not null ||
                                   expectedEffectDirection is not null ||
                                   request.MinimumEffect is not null;
        if (hasValidationCriterion &&
            (validationOutcomeCode is null || expectedEffectDirection is null ||
             request.MinimumEffect is not { } minimumEffect ||
             !objectiveCodes.Contains(validationOutcomeCode) ||
             !ResearchHypothesisEffectDirections.IsValid(expectedEffectDirection) ||
             !double.IsFinite(minimumEffect) || minimumEffect <= 0))
        {
            throw new ProcessResearchRuleException(
                "假设验证必须同时定义项目目标、预期效应方向和正的最小效应。");
        }
        if (!ResearchHypothesisStatuses.IsValid(request.Status))
            throw new ProcessResearchRuleException("研发假设状态无效。");
        if (request.Status == ResearchHypothesisStatuses.Validated &&
            existing?.Status != ResearchHypothesisStatuses.Validated)
            throw new ProcessResearchRuleException("已验证原因只能由跨区组重复干预实验自动确认。");
        if (request.Confidence is < 0 or > 1 || !double.IsFinite(request.Confidence))
            throw new ProcessResearchRuleException("研发假设置信度必须位于 0 到 1 之间。");

        var value = request with
        {
            HypothesisId = existing?.HypothesisId ??
                           (request.HypothesisId == Guid.Empty
                               ? Guid.CreateVersion7()
                               : request.HypothesisId),
            ProjectId = projectId,
            Statement = statement,
            Rationale = rationale,
            VariableCodes = variableCodes,
            ValidationOutcomeCode = validationOutcomeCode,
            ExpectedEffectDirection = expectedEffectDirection,
            MinimumEffect = request.MinimumEffect,
            PossibleConfounders = NormalizeTextList(request.PossibleConfounders, "可能混杂因素", 240),
            Applicability = OptionalText(request.Applicability, 8000),
            SupportingEvidence = NormalizeEvidence(projectId, request.SupportingEvidence),
            OpposingEvidence = NormalizeEvidence(projectId, request.OpposingEvidence),
            ValidationEvidence = NormalizeEvidence(projectId, request.ValidationEvidence),
            CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        var saved = await store.SaveHypothesisAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(projectId, "hypothesis", saved.HypothesisId.ToString(),
            existing is null ? "created" : "updated", userId, existing?.Status, saved.Status, ct)
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchExperiment> CreateExperimentAsync(
        Guid projectId,
        ResearchExperiment request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        ResearchProcessWindow? validationWindow = null;
        if (request.ValidationWindowId is { } validationWindowId)
        {
            validationWindow = await store.GetProcessWindowAsync(validationWindowId, ct)
                .ConfigureAwait(false);
            if (validationWindow is null ||
                validationWindow.ProjectId != projectId ||
                validationWindow.Status != ProcessWindowStatuses.Candidate)
                throw new ProcessResearchRuleException("独立验证实验必须引用当前项目中的候选工艺窗口。");
        }
        if (request.HypothesisId is { } hypothesisId)
        {
            var hypothesis = await store.GetHypothesisAsync(hypothesisId, ct).ConfigureAwait(false);
            if (hypothesis is null || hypothesis.ProjectId != projectId)
                throw new ProcessResearchRuleException("实验引用的研发假设不存在于当前项目。");
        }

        var knownVariables = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var designMethod = RequiredText(request.DesignMethod, "实验设计方法", 120)
            .ToLowerInvariant();
        if (!ResearchDesignMethods.IsValid(designMethod))
            throw new ProcessResearchRuleException("实验设计方法无效。");
        if (request.RunPlan.Count < (validationWindow is null ? 2 : 3))
            throw new ProcessResearchRuleException(
                validationWindow is null
                    ? "实验计划必须至少包含两个运行条件，不能用单点设置代替实验设计。"
                    : "独立验证实验至少需要三个重复运行。");

        ExperimentFactorSetting NormalizeFactor(ExperimentFactorSetting value)
        {
            var code = NormalizeCode(value.VariableCode, "实验变量");
            if (!knownVariables.TryGetValue(code, out var variable))
                throw new ProcessResearchRuleException($"实验变量 {code} 不是项目中的可控变量。");
            if (!double.IsFinite(value.Value) ||
                variable.LowerLimit is { } lower && value.Value < lower ||
                variable.UpperLimit is { } upper && value.Value > upper)
                throw new ProcessResearchRuleException($"实验变量 {code} 超出允许范围。");
            var unit = RequiredText(value.Unit, "实验变量单位", 40);
            if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"实验变量 {code} 的单位必须与项目变量一致。");
            return value with { VariableCode = code, Unit = unit };
        }

        var runPlan = request.RunPlan.Select((run, index) =>
        {
            var factors = run.Factors.Select(NormalizeFactor).ToArray();
            if (factors.Length == 0 ||
                factors.Select(static value => value.VariableCode)
                    .Distinct(StringComparer.Ordinal).Count() != factors.Length)
                throw new ProcessResearchRuleException("每个实验运行必须包含不重复的可控变量设置。");
            return run with
            {
                RunKey = RequiredText(run.RunKey, "实验运行标识", 120),
                Sequence = run.Sequence > 0 ? run.Sequence : index + 1,
                BlockKey = OptionalText(run.BlockKey, 120),
                ReplicateKey = OptionalText(run.ReplicateKey, 120),
                Factors = factors
            };
        }).ToArray();
        if (runPlan.Select(static value => value.RunKey).Distinct(StringComparer.Ordinal).Count() !=
            runPlan.Length ||
            runPlan.Select(static value => value.Sequence).Distinct().Count() != runPlan.Length)
            throw new ProcessResearchRuleException("实验运行标识和执行顺序必须唯一。");
        var baselineRunKeys = request.BaselineRunKeys
            .Select(value => RequiredText(value, "对照运行标识", 120))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (baselineRunKeys.Length != request.BaselineRunKeys.Count)
            throw new ProcessResearchRuleException("对照运行标识不能重复。");
        if (baselineRunKeys.Length == 1)
            throw new ProcessResearchRuleException("生成独立对照置信区间至少需要两个对照运行。");
        if (baselineRunKeys.Length > 0)
        {
            var currentRunKeys = runPlan.Select(static value => value.RunKey)
                .ToHashSet(StringComparer.Ordinal);
            if (currentRunKeys.All(baselineRunKeys.Contains))
                throw new ProcessResearchRuleException("实验必须至少保留一个非对照运行用于效果比较。");
            var eligiblePriorRunKeys = (await store.ListExperimentsAsync(projectId, ct)
                    .ConfigureAwait(false))
                .Where(static value =>
                    value.DesignMethod == ResearchDesignMethods.HistoricalObservation ||
                    value.Status == ResearchExperimentStatuses.Completed)
                .SelectMany(static value => value.RunPlan)
                .Select(static value => value.RunKey)
                .ToHashSet(StringComparer.Ordinal);
            if (baselineRunKeys.Any(key =>
                    !currentRunKeys.Contains(key) && !eligiblePriorRunKeys.Contains(key)))
                throw new ProcessResearchRuleException(
                    "对照运行必须来自本实验、已导入的历史观察或已完成实验。");
        }
        var distinctConditions = runPlan
            .Select(run => string.Join("|", run.Factors
                .OrderBy(static factor => factor.VariableCode)
                .Select(static factor => $"{factor.VariableCode}:{factor.Value:R}")))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctConditions < 2 && validationWindow is null)
            throw new ProcessResearchRuleException("实验至少需要两个不同的变量组合。");
        if (validationWindow is not null && distinctConditions != 1)
            throw new ProcessResearchRuleException("独立验证实验必须重复验证同一个候选设置，不能同时改变条件。");

        var factors = (request.Factors.Count > 0
                ? request.Factors.Select(NormalizeFactor)
                : runPlan.SelectMany(static run => run.Factors)
                    .GroupBy(static factor => factor.VariableCode, StringComparer.Ordinal)
                    .Select(static group => group.First()))
            .ToArray();
        if (factors.Select(static value => value.VariableCode).Distinct(StringComparer.Ordinal).Count() !=
            factors.Length)
            throw new ProcessResearchRuleException("同一实验变量只能设置一次。");

        var objectiveCodes = NormalizeCodes(request.ObjectiveCodes, "实验目标");
        var knownObjectives = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Count == 0 || objectiveCodes.Any(code => !knownObjectives.Contains(code)))
            throw new ProcessResearchRuleException("实验必须引用项目中已经定义的目标。");
        if (request.Optimization is not null)
        {
            if (designMethod != ResearchDesignMethods.BayesianOptimization)
                throw new ProcessResearchRuleException("优化元数据只能附加到贝叶斯优化实验。");
            if (!Regex.IsMatch(
                    request.Optimization.InputHash,
                    "^[a-f0-9]{64}$",
                    RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(request.Optimization.ModelVersion) ||
                request.Optimization.RunPredictions.Count != runPlan.Length ||
                !request.Optimization.RunPredictions.Select(static value => value.RunKey)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(runPlan.Select(static value => value.RunKey)))
                throw new ProcessResearchRuleException("优化实验的模型版本、输入摘要或运行预测无效。");
        }

        var now = DateTimeOffset.UtcNow;
        var value = request with
        {
            ExperimentId = request.ExperimentId == Guid.Empty
                ? Guid.CreateVersion7()
                : request.ExperimentId,
            ProjectId = projectId,
            ValidationWindowId = validationWindow?.WindowId,
            Name = RequiredText(request.Name, "实验名称", 240),
            DesignMethod = designMethod,
            PlanVersion = 1,
            ProjectRevision = project.Revision,
            RandomizationSeed = request.RandomizationSeed == 0
                ? RandomNumberGenerator.GetInt32(1, int.MaxValue)
                : request.RandomizationSeed,
            BlockingKeys = request.BlockingKeys.Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Status = ResearchExperimentStatuses.Planned,
            Factors = factors,
            RunPlan = runPlan,
            BaselineRunKeys = baselineRunKeys,
            ObjectiveCodes = objectiveCodes,
            ReplicateKeys = request.ReplicateKeys.Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ResultIds = [],
            Execution = new ResearchExperimentExecution
            {
                DispatchId = Guid.CreateVersion7(),
                State = ResearchExperimentExecutionStates.AwaitingApproval,
                Commands = runPlan.Select(run => new ExperimentExecutionCommand
                {
                    CommandId = Guid.CreateVersion7(),
                    RunKey = run.RunKey,
                    Sequence = run.Sequence,
                    BlockKey = run.BlockKey,
                    ReplicateKey = run.ReplicateKey,
                    RequestedFactors = run.Factors
                }).ToArray()
            },
            StopRule = RequiredText(request.StopRule, "停止规则", 4000),
            RollbackPlan = RequiredText(request.RollbackPlan, "回退方案", 4000),
            CreatedBy = NormalizeUser(userId),
            ApprovedBy = null,
            ApprovedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (await store.GetExperimentAsync(value.ExperimentId, ct).ConfigureAwait(false) is not null)
            throw new ProcessResearchRuleException("实验标识已经存在。");
        var saved = await store.SaveExperimentAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(projectId, "experiment", saved.ExperimentId.ToString(), "planned",
            userId, null, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchExperiment> ChangeExperimentStatusAsync(
        Guid experimentId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实验不存在。");
        await RequireMutableProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false);
        var actor = NormalizeUser(userId);
        targetStatus = NormalizeStatus(targetStatus, ResearchExperimentStatuses.IsValid, "实验状态");
        if (experiment.Status == targetStatus)
            return experiment;
        var allowed = (experiment.Status, targetStatus) switch
        {
            (ResearchExperimentStatuses.Planned, ResearchExperimentStatuses.Approved) => true,
            (ResearchExperimentStatuses.Approved, ResearchExperimentStatuses.Running) => true,
            (ResearchExperimentStatuses.Running, ResearchExperimentStatuses.Completed) => true,
            (_, ResearchExperimentStatuses.Cancelled)
                when experiment.Status is ResearchExperimentStatuses.Planned
                    or ResearchExperimentStatuses.Approved
                    or ResearchExperimentStatuses.Running => true,
            _ => false
        };
        if (!allowed)
            throw new ProcessResearchRuleException(
                $"实验状态不能从 {experiment.Status} 转换为 {targetStatus}。");
        if (targetStatus == ResearchExperimentStatuses.Approved &&
            string.Equals(experiment.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("实验创建人和批准人必须分离。");
        if (targetStatus == ResearchExperimentStatuses.Running &&
            experiment.ProjectRevision !=
            (await RequireProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false)).Revision)
            throw new ProcessResearchRuleException("项目定义已变化，请基于最新变量和目标重新制定实验计划。");
        if (targetStatus == ResearchExperimentStatuses.Completed)
        {
            var results = (await store.ListExperimentResultsAsync(experiment.ProjectId, ct)
                    .ConfigureAwait(false))
                .Where(value => value.ExperimentId == experimentId)
                .ToArray();
            if (results.Length == 0)
                throw new ProcessResearchRuleException("实验完成前必须记录由源数据计算得到的结果。");
            if (results.Any(static value => !value.CalculatedFromSource || !value.SafetyPassed))
                throw new ProcessResearchRuleException("实验结果必须来自源数据计算且通过安全约束检查。");
            var coveredObjectives = results.SelectMany(static value => value.Metrics)
                .Select(static value => value.ObjectiveCode)
                .ToHashSet(StringComparer.Ordinal);
            if (experiment.ObjectiveCodes.Any(code => !coveredObjectives.Contains(code)))
                throw new ProcessResearchRuleException("实验结果尚未覆盖全部实验目标。");
        }

        var saved = await store.SaveExperimentAsync(
            experiment with
            {
                Status = targetStatus,
                Execution = UpdateExecution(experiment, targetStatus, actor),
                ApprovedBy = targetStatus == ResearchExperimentStatuses.Approved
                    ? actor
                    : experiment.ApprovedBy,
                ApprovedAt = targetStatus == ResearchExperimentStatuses.Approved
                    ? DateTimeOffset.UtcNow
                    : experiment.ApprovedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(experiment.ProjectId, "experiment", experimentId.ToString(),
            "status-changed", userId, experiment.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

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
                RunKey = RequiredText(observation.RunKey, "运行观察标识", 240),
                ActualFactors = factors,
                SourceContentHash = sourceHash,
                ExclusionReason = OptionalText(observation.ExclusionReason, 1000)
            };
        }).ToArray();
        if (runObservations.Select(static value => value.RunKey)
                .Distinct(StringComparer.Ordinal).Count() != runObservations.Length)
            throw new ProcessResearchRuleException("同一结果中的运行观察标识不能重复。");
        if (!runObservations.Select(static value => value.RunKey)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(experiment.RunPlan.Select(static value => value.RunKey)))
            throw new ProcessResearchRuleException("实验结果必须包含计划中每个 RunKey 的逐运行源数据观察。");

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
        var distinctEquipmentCount = CountDistinctContext(runObservations, "machine_id");
        var safetyPassed = project.OutcomeConstraints.All(constraint =>
            runObservations.All(observation =>
                observation.ConstraintOutcomes.TryGetValue(constraint.Code, out var outcome) &&
                (constraint.Operator == "<="
                    ? outcome <= constraint.Limit
                    : outcome >= constraint.Limit)));
        var excludedRunKeys = runObservations
            .Where(static value => !value.ValidForOptimization)
            .Select(static value => value.RunKey)
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
            ExcludedRunKeys = excludedRunKeys
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
            ExcludedRunKeys = excludedRunKeys,
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

    public async Task<ResearchProcessWindow> SaveProcessWindowAsync(
        Guid projectId,
        ResearchProcessWindow request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        var knownVariables = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (request.Variables.Count == 0)
            throw new ProcessResearchRuleException("工艺窗口必须包含至少一个可控变量范围。");
        var variables = request.Variables.Select(value =>
        {
            var code = NormalizeCode(value.VariableCode, "工艺窗口变量");
            if (!knownVariables.TryGetValue(code, out var variable))
                throw new ProcessResearchRuleException($"工艺窗口变量 {code} 不是项目中的可控变量。");
            if (!double.IsFinite(value.LowerBound) || !double.IsFinite(value.UpperBound) ||
                value.LowerBound > value.UpperBound ||
                variable.LowerLimit is { } lower && value.LowerBound < lower ||
                variable.UpperLimit is { } upper && value.UpperBound > upper)
                throw new ProcessResearchRuleException($"工艺窗口变量 {code} 的范围无效。");
            var unit = RequiredText(value.Unit, "工艺窗口变量单位", 40);
            if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"工艺窗口变量 {code} 的单位必须与项目变量一致。");
            return value with
            {
                VariableCode = code,
                Unit = unit
            };
        }).ToArray();
        if (variables.Select(static value => value.VariableCode).Distinct(StringComparer.Ordinal).Count() !=
            variables.Length)
            throw new ProcessResearchRuleException("工艺窗口中的变量不能重复。");
        if (request.SupportingExperimentIds.Count == 0)
            throw new ProcessResearchRuleException("候选工艺窗口必须关联验证实验。");
        foreach (var experimentId in request.SupportingExperimentIds.Distinct())
        {
            var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
            if (experiment is null || experiment.ProjectId != projectId ||
                experiment.Status != ResearchExperimentStatuses.Completed)
                throw new ProcessResearchRuleException("工艺窗口只能引用当前项目中已完成的实验。");
        }
        if (request.SupportingResultIds.Count == 0)
            throw new ProcessResearchRuleException("候选工艺窗口必须关联实验计算结果。");
        var supportingResults = new List<ResearchExperimentResult>();
        foreach (var resultId in request.SupportingResultIds.Distinct())
        {
            var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false);
            if (result is null || result.ProjectId != projectId ||
                !request.SupportingExperimentIds.Contains(result.ExperimentId) ||
                !result.CalculatedFromSource || !result.SafetyPassed)
                throw new ProcessResearchRuleException("工艺窗口只能引用当前项目中通过安全检查的源数据计算结果。");
            supportingResults.Add(result);
        }
        var objectiveCodes = NormalizeCodes(request.ObjectiveCodes, "工艺窗口目标");
        var knownObjectives = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Count == 0 || objectiveCodes.Any(code => !knownObjectives.Contains(code)))
            throw new ProcessResearchRuleException("工艺窗口必须引用项目中已经定义的目标。");
        var coveredObjectives = supportingResults.SelectMany(static value => value.Metrics)
            .Select(static value => value.ObjectiveCode)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Any(code => !coveredObjectives.Contains(code)))
            throw new ProcessResearchRuleException("工艺窗口的计算结果尚未覆盖全部目标。");
        if (request.Confidence is <= 0 or > 1 || !double.IsFinite(request.Confidence))
            throw new ProcessResearchRuleException("工艺窗口置信度必须大于 0 且不超过 1。");
        var confidenceMethod = RequiredText(request.ConfidenceMethod, "置信度计算方法", 120)
            .ToLowerInvariant();
        if (!ResearchConfidenceMethods.IsValid(confidenceMethod))
            throw new ProcessResearchRuleException("工艺窗口置信度计算方法无效。");
        if (request.AnalysisRunId == Guid.Empty || !HashPattern().IsMatch(request.AnalysisHash))
            throw new ProcessResearchRuleException("工艺窗口必须关联可追溯的分析运行和 SHA-256 摘要。");
        if (supportingResults.All(result =>
                result.AnalysisRunId != request.AnalysisRunId ||
                !string.Equals(
                    result.AnalysisHash,
                    request.AnalysisHash,
                    StringComparison.OrdinalIgnoreCase)))
            throw new ProcessResearchRuleException("工艺窗口的分析运行必须来自所关联的实验结果。");

        var now = DateTimeOffset.UtcNow;
        var existing = request.WindowId == Guid.Empty
            ? null
            : await store.GetProcessWindowAsync(request.WindowId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("工艺窗口不属于当前项目。");
        if (existing?.Status == ProcessWindowStatuses.Validated)
            throw new ProcessResearchRuleException("经过验证的工艺窗口保持不可变。");

        var evidence = NormalizeEvidence(projectId, request.Evidence).ToList();
        foreach (var result in supportingResults)
        {
            evidence.Add(CreateEvidence(
                projectId,
                EvidenceKinds.ExperimentResult,
                result.ResultId.ToString(),
                "支持该工艺窗口的实验结果。",
                result.AnalysisHash,
                now));
        }
        evidence.Add(CreateEvidence(
            projectId,
            EvidenceKinds.AnalysisRun,
            request.AnalysisRunId.ToString(),
            "生成候选工艺窗口的分析运行。",
            request.AnalysisHash.ToLowerInvariant(),
            now));

        var saved = await store.SaveProcessWindowAsync(
            request with
            {
                WindowId = existing?.WindowId ??
                           (request.WindowId == Guid.Empty ? Guid.CreateVersion7() : request.WindowId),
                ProjectId = projectId,
                Name = RequiredText(request.Name, "工艺窗口名称", 240),
                Status = ProcessWindowStatuses.Candidate,
                Variables = variables,
                ObjectiveCodes = objectiveCodes,
                SupportingExperimentIds = request.SupportingExperimentIds.Distinct().ToArray(),
                SupportingResultIds = request.SupportingResultIds.Distinct().ToArray(),
                Evidence = evidence
                    .GroupBy(static value => (value.Kind, value.ReferenceId))
                    .Select(static group => group.First())
                    .ToArray(),
                ConfidenceMethod = confidenceMethod,
                AnalysisHash = request.AnalysisHash.ToLowerInvariant(),
                Applicability = RequiredText(request.Applicability, "工艺窗口适用范围", 8000),
                ValidationLevel = ProcessWindowValidationLevels.Evidence,
                ValidationNotes = null,
                ValidatedBy = null,
                ValidatedAt = null,
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "process-window", saved.WindowId.ToString(),
            existing is null ? "candidate-created" : "candidate-updated",
            userId, existing?.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchExperiment> CreateProcessWindowValidationExperimentAsync(
        Guid windowId,
        string userId,
        CancellationToken ct = default)
    {
        var window = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺窗口不存在。");
        var project = await RequireMutableProjectAsync(window.ProjectId, ct).ConfigureAwait(false);
        if (window.Status != ProcessWindowStatuses.Candidate)
            throw new ProcessResearchRuleException("只有候选工艺窗口可以设计独立验证实验。");
        if (project.Status != ResearchProjectStatuses.Validating)
            throw new ProcessResearchRuleException("项目进入验证阶段后才能设计独立验证实验。");
        if (window.Variables.Any(static value => value.LowerBound != value.UpperBound))
            throw new ProcessResearchRuleException(
                "当前自动验证只支持候选设置点；连续工艺窗口必须先完成覆盖边界和交互作用的扩展实验。");

        var experiments = await store.ListExperimentsAsync(project.ProjectId, ct)
            .ConfigureAwait(false);
        var existing = experiments
            .Where(value => value.ValidationWindowId == windowId &&
                            value.Status != ResearchExperimentStatuses.Cancelled)
            .OrderByDescending(static value => value.CreatedAt)
            .FirstOrDefault();
        if (existing is not null)
            return existing;

        var experimentId = Guid.CreateVersion7();
        var shortId = experimentId.ToString("N")[..8];
        var factors = window.Variables
            .Select(variable => new ExperimentFactorSetting
            {
                VariableCode = variable.VariableCode,
                Value = (variable.LowerBound + variable.UpperBound) / 2,
                Unit = variable.Unit
            })
            .ToArray();
        var runs = Enumerable.Range(1, 3)
            .Select(index => new ExperimentRunPlan
            {
                RunKey = $"validation-{shortId}-r{index:00}",
                Sequence = index,
                BlockKey = $"validation-block-{index:00}",
                ReplicateKey = "candidate-setting",
                Factors = factors
            })
            .ToArray();
        return await CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                ExperimentId = experimentId,
                ValidationWindowId = windowId,
                Name = $"独立验证 · {window.Name}",
                DesignMethod = ResearchDesignMethods.EngineerDefined,
                Factors = factors,
                RunPlan = runs,
                BlockingKeys = runs.Select(static value => value.BlockKey!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                ReplicateKeys = ["candidate-setting"],
                ObjectiveCodes = window.ObjectiveCodes,
                StopRule = "任一安全约束失败立即停止；三个跨区组重复全部完成后结束。",
                RollbackPlan = "停止验证并恢复验证前已批准的生产配方；候选窗口保持未验证状态。"
            },
            userId,
            ct).ConfigureAwait(false);
    }

    public async Task<ResearchProcessWindow> AttachProcessWindowValidationResultAsync(
        Guid windowId,
        ResearchExperiment experiment,
        ResearchExperimentResult result,
        string userId,
        CancellationToken ct = default)
    {
        var window = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺窗口不存在。");
        var project = await RequireMutableProjectAsync(window.ProjectId, ct).ConfigureAwait(false);
        var persistedExperiment = await store.GetExperimentAsync(experiment.ExperimentId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("独立验证实验不存在。");
        if (window.Status != ProcessWindowStatuses.Candidate ||
            persistedExperiment.ValidationWindowId != windowId ||
            persistedExperiment.ProjectId != window.ProjectId ||
            persistedExperiment.Status != ResearchExperimentStatuses.Completed ||
            result.ExperimentId != persistedExperiment.ExperimentId ||
            result.ProjectId != window.ProjectId)
            throw new ProcessResearchRuleException("验证结果与候选工艺窗口不匹配。");
        if (!result.CalculatedFromSource || !result.SafetyPassed ||
            result.RunCount < 3 || result.ReplicateCount < 3 ||
            result.DistinctBlockCount < 2 || result.RunObservations.Count < 3)
            throw new ProcessResearchRuleException("独立验证至少需要三个源数据重复运行、两个区组且全部通过安全约束。");
        if (window.Variables.Any(static value => value.LowerBound != value.UpperBound))
            throw new ProcessResearchRuleException(
                "单点重复结果不能验证连续工艺窗口；请先完成覆盖边界和交互作用的扩展实验。");
        if (result.RunObservations.Any(observation =>
                !observation.ValidForOptimization ||
                !IsInsideWindow(window, observation) ||
                !MeetsMeasuredSpecification(project, observation)))
            throw new ProcessResearchRuleException("独立验证运行未全部位于候选设置内并满足目标与安全约束。");

        if (window.SupportingResultIds.Contains(result.ResultId))
            return window;
        var now = DateTimeOffset.UtcNow;
        var evidence = window.Evidence.Append(CreateEvidence(
                window.ProjectId,
                EvidenceKinds.ExperimentResult,
                result.ResultId.ToString(),
                "候选工艺窗口的独立跨区组重复验证结果。",
                result.AnalysisHash,
                now))
            .GroupBy(static value => (value.Kind, value.ReferenceId))
            .Select(static group => group.First())
            .ToArray();
        var validationConfidence = WilsonLowerBound(result.RunCount, result.RunCount);
        var saved = await store.SaveProcessWindowAsync(
            window with
            {
                SupportingExperimentIds = window.SupportingExperimentIds
                    .Append(persistedExperiment.ExperimentId).Distinct().ToArray(),
                SupportingResultIds = window.SupportingResultIds
                    .Append(result.ResultId).Distinct().ToArray(),
                Evidence = evidence,
                Confidence = Math.Min(window.Confidence, validationConfidence),
                ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
                ValidationNotes = "已完成独立跨区组重复实验，等待与候选窗口创建人分离的工程师复核。",
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(window.ProjectId, "process-window", windowId.ToString(),
            "validation-result-attached", userId, window.Status, saved.Status, ct)
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProcessWindow> ValidateProcessWindowAsync(
        Guid windowId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺窗口不存在。");
        var project = await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        if (value.Status != ProcessWindowStatuses.Candidate)
            throw new ProcessResearchRuleException("只有候选工艺窗口可以进入验证状态。");
        if (project.Status != ResearchProjectStatuses.Validating)
            throw new ProcessResearchRuleException("项目进入验证阶段后才能批准工艺窗口。");
        var actor = NormalizeUser(userId);
        if (string.Equals(value.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("工艺窗口创建人和验证人必须分离。");
        if (value.Evidence.Count == 0 || value.Confidence <= 0 ||
            value.SupportingResultIds.Count == 0)
            throw new ProcessResearchRuleException("工艺窗口验证需要可追溯实验结果和统计置信度。");
        if (value.Variables.Any(static variable => variable.LowerBound != variable.UpperBound))
            throw new ProcessResearchRuleException(
                "连续工艺窗口不能由单点重复验证直接批准；请先完成覆盖边界和交互作用的扩展实验。");
        var experiments = await store.ListExperimentsAsync(value.ProjectId, ct)
            .ConfigureAwait(false);
        var validationExperimentIds = experiments
            .Where(experiment =>
                experiment.ValidationWindowId == windowId &&
                experiment.Status == ResearchExperimentStatuses.Completed)
            .Select(static experiment => experiment.ExperimentId)
            .ToHashSet();
        var validationResults = new List<ResearchExperimentResult>();
        foreach (var resultId in value.SupportingResultIds)
        {
            var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false);
            if (result is null || result.ProjectId != value.ProjectId ||
                !result.CalculatedFromSource || !result.SafetyPassed)
                throw new ProcessResearchRuleException("工艺窗口的支持结果已失效，不能通过验证。");
            if (validationExperimentIds.Contains(result.ExperimentId))
                validationResults.Add(result);
        }
        if (validationResults.Count == 0 ||
            validationResults.Any(result =>
                result.RunCount < 3 ||
                result.ReplicateCount < 3 ||
                result.DistinctBlockCount < 2))
            throw new ProcessResearchRuleException("请先完成独立验证实验；同一批候选生成数据不能替代验证证据。");
        var saved = await store.SaveProcessWindowAsync(
            value with
            {
                Status = ProcessWindowStatuses.Validated,
                ValidationLevel = ProcessWindowValidationLevels.Laboratory,
                ValidationNotes = $"由 {actor} 独立复核跨区组重复实验、适用范围与安全约束。",
                ValidatedBy = actor,
                ValidatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "process-window", windowId.ToString(), "validated",
            userId, value.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProcessWindow> ReleaseProcessWindowAsync(
        Guid windowId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺窗口不存在。");
        await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        if (value.Status != ProcessWindowStatuses.Validated ||
            value.ValidationLevel != ProcessWindowValidationLevels.Laboratory)
            throw new ProcessResearchRuleException("只有通过跨区组重复实验验证的工艺窗口才能发布生产。");
        var actor = NormalizeUser(userId);
        if (string.Equals(value.ValidatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("实验室验证人与生产发布人必须分离。");
        var saved = await store.SaveProcessWindowAsync(
            value with
            {
                ValidationLevel = ProcessWindowValidationLevels.Production,
                ValidationNotes = $"{value.ValidationNotes} 由 {actor} 审核并发布生产。",
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "process-window", windowId.ToString(),
            "production-released", userId, value.ValidationLevel, saved.ValidationLevel, ct)
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        Guid projectId,
        ResearchKnowledgeClaim request,
        string userId,
        CancellationToken ct = default)
    {
        await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        ResearchProcessWindow? referencedWindow = null;
        if (request.ProcessWindowId is { } windowId)
        {
            referencedWindow = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false);
            if (referencedWindow is null || referencedWindow.ProjectId != projectId ||
                referencedWindow.Status != ProcessWindowStatuses.Validated ||
                referencedWindow.ValidationLevel is not (
                    ProcessWindowValidationLevels.Laboratory or
                    ProcessWindowValidationLevels.Production))
                throw new ProcessResearchRuleException(
                    "知识声明只能引用经过跨区组重复实验验证的工艺窗口。");
        }
        var now = DateTimeOffset.UtcNow;
        var existing = request.ClaimId == Guid.Empty
            ? null
            : await store.GetKnowledgeClaimAsync(request.ClaimId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("知识声明不属于当前项目。");
        if (existing?.Status is ResearchKnowledgeStatuses.Published or ResearchKnowledgeStatuses.Retired)
            throw new ProcessResearchRuleException("已发布或已停用的知识声明保持不可变。");

        var evidence = NormalizeEvidence(projectId, request.Evidence).ToList();
        if (referencedWindow is not null)
        {
            evidence.Add(CreateEvidence(
                projectId,
                EvidenceKinds.ProcessWindow,
                referencedWindow.WindowId.ToString(),
                "知识声明引用的已验证工艺窗口。",
                referencedWindow.AnalysisHash,
                now));
        }
        var saved = await store.SaveKnowledgeClaimAsync(
            request with
            {
                ClaimId = existing?.ClaimId ??
                          (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId),
                ProjectId = projectId,
                Statement = RequiredText(request.Statement, "知识声明", 8000),
                Applicability = RequiredText(request.Applicability, "知识适用范围", 8000),
                Status = ResearchKnowledgeStatuses.Draft,
                Evidence = evidence
                    .GroupBy(static value => (value.Kind, value.ReferenceId))
                    .Select(static group => group.First())
                    .ToArray(),
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                ReviewedBy = null,
                ReviewedAt = null,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "knowledge-claim", saved.ClaimId.ToString(),
            existing is null ? "created" : "updated",
            userId, existing?.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchKnowledgeClaim> ReviewKnowledgeClaimAsync(
        Guid claimId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetKnowledgeClaimAsync(claimId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("知识声明不存在。");
        await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        var actor = NormalizeUser(userId);
        if (value.Status != ResearchKnowledgeStatuses.Draft)
            throw new ProcessResearchRuleException("只有草稿知识声明可以审核。");
        if (string.Equals(value.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("知识声明创建人和审核人必须分离。");
        if (value.Evidence.Count == 0)
            throw new ProcessResearchRuleException("知识声明审核前必须关联证据。");

        var saved = await store.SaveKnowledgeClaimAsync(
            value with
            {
                Status = ResearchKnowledgeStatuses.Reviewed,
                ReviewedBy = actor,
                ReviewedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "knowledge-claim", claimId.ToString(), "reviewed",
            userId, value.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProjectWorkspace> GetWorkspaceAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        var hypothesesTask = store.ListHypothesesAsync(projectId, ct);
        var experimentsTask = store.ListExperimentsAsync(projectId, ct);
        var resultsTask = store.ListExperimentResultsAsync(projectId, ct);
        var windowsTask = store.ListProcessWindowsAsync(projectId, ct);
        var claimsTask = store.ListKnowledgeClaimsAsync(projectId, ct);
        var auditTask = store.ListAuditEntriesAsync(projectId, ct);
        await Task.WhenAll(
            hypothesesTask,
            experimentsTask,
            resultsTask,
            windowsTask,
            claimsTask,
            auditTask).ConfigureAwait(false);
        return new ResearchProjectWorkspace
        {
            Project = project,
            Hypotheses = await hypothesesTask.ConfigureAwait(false),
            Experiments = await experimentsTask.ConfigureAwait(false),
            ExperimentResults = await resultsTask.ConfigureAwait(false),
            ProcessWindows = await windowsTask.ConfigureAwait(false),
            KnowledgeClaims = await claimsTask.ConfigureAwait(false),
            Audit = await auditTask.ConfigureAwait(false)
        };
    }

    private async Task<ResearchProject> RequireProjectAsync(Guid projectId, CancellationToken ct)
        => await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
           ?? throw new ProcessResearchRuleException("研发项目不存在。");

    private async Task<ResearchProject> RequireMutableProjectAsync(
        Guid projectId,
        CancellationToken ct)
    {
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        return project;
    }

    private static ResearchProject NormalizeProject(ResearchProject value)
    {
        if (!ResearchProjectStatuses.IsValid(value.Status))
            throw new ProcessResearchRuleException("研发项目状态无效。");
        var code = NormalizeCode(value.Code, "研发项目代码");
        var objectives = value.Objectives.Select(NormalizeObjective).ToArray();
        if (objectives.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() !=
            objectives.Length)
            throw new ProcessResearchRuleException("研发目标代码不能重复。");
        var variables = value.Variables.Select(NormalizeVariable).ToArray();
        if (variables.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() !=
            variables.Length)
            throw new ProcessResearchRuleException("工艺变量代码不能重复。");
        var knownVariables = variables.Select(static item => item.Code).ToHashSet(StringComparer.Ordinal);
        var optimizationFeatures = NormalizeOptimizationFeatures(
            value.OptimizationFeatures,
            variables.Where(static item => item.Role == ResearchVariableRoles.Control)
                .Select(static item => item.Code));
        var constraints = value.Constraints.Select(item =>
        {
            var variableCode = NormalizeCode(item.VariableCode, "约束变量");
            if (!knownVariables.Contains(variableCode))
                throw new ProcessResearchRuleException($"约束引用了未定义变量 {variableCode}。");
            if (!double.IsFinite(item.Limit))
                throw new ProcessResearchRuleException("约束限值必须是有限数值。");
            var constraintOperator = item.Operator.Trim();
            if (constraintOperator is not ("<=" or ">="))
                throw new ProcessResearchRuleException("参数约束操作符必须是 <= 或 >=。");
            return item with
            {
                Code = NormalizeCode(item.Code, "约束代码"),
                Description = RequiredText(item.Description, "约束说明", 1000),
                VariableCode = variableCode,
                Operator = constraintOperator,
                Unit = RequiredText(item.Unit, "约束单位", 40)
            };
        }).ToArray();
        if (constraints.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() !=
            constraints.Length)
            throw new ProcessResearchRuleException("约束代码不能重复。");
        var outcomeConstraints = value.OutcomeConstraints.Select(item =>
        {
            if (!double.IsFinite(item.Limit) ||
                !double.IsFinite(item.MinimumProbability) ||
                item.MinimumProbability is <= 0 or > 1)
                throw new ProcessResearchRuleException("结果约束限值或最低可行概率无效。");
            var constraintOperator = item.Operator.Trim();
            if (constraintOperator is not ("<=" or ">="))
                throw new ProcessResearchRuleException("结果约束操作符必须是 <= 或 >=。");
            return item with
            {
                Code = NormalizeCode(item.Code, "结果约束代码"),
                Description = RequiredText(item.Description, "结果约束说明", 1000),
                OutcomeCode = NormalizeCode(item.OutcomeCode, "结果约束指标"),
                Operator = constraintOperator,
                Unit = RequiredText(item.Unit, "结果约束单位", 40),
                DataSource = OptionalText(item.DataSource, 500)
            };
        }).ToArray();
        if (outcomeConstraints.Select(static item => item.Code)
                .Distinct(StringComparer.Ordinal).Count() != outcomeConstraints.Length)
            throw new ProcessResearchRuleException("结果约束代码不能重复。");
        if (outcomeConstraints.Select(static item => item.Code)
            .Intersect(objectives.Select(static item => item.Code), StringComparer.Ordinal).Any())
            throw new ProcessResearchRuleException("研发目标代码与结果约束代码不能重复。");

        return value with
        {
            Code = code,
            Name = RequiredText(value.Name, "研发项目名称", 240),
            ProcessName = RequiredText(value.ProcessName, "工艺名称", 240),
            ProductName = OptionalText(value.ProductName, 240),
            MaterialName = OptionalText(value.MaterialName, 240),
            Description = OptionalText(value.Description, 8000),
            Objectives = objectives,
            Variables = variables,
            Constraints = constraints,
            OutcomeConstraints = outcomeConstraints,
            OptimizationFeatures = optimizationFeatures,
            OwnerUserId = NormalizeUser(value.OwnerUserId),
            MemberUserIds = value.MemberUserIds
                .Append(value.OwnerUserId)
                .Select(NormalizeUser)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            SiteCode = OptionalText(value.SiteCode, 120)?.ToLowerInvariant(),
            Context = value.Context
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) &&
                                      !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    static pair => pair.Key.Trim().ToLowerInvariant(),
                    static pair => pair.Value.Trim(),
                    StringComparer.Ordinal)
        };
    }

    private static ResearchOptimizationFeatureSet NormalizeOptimizationFeatures(
        ResearchOptimizationFeatureSet? value,
        IEnumerable<string> controlVariableCodes)
    {
        value ??= new ResearchOptimizationFeatureSet();
        if (value.Version < 1)
            throw new ProcessResearchRuleException("优化特征集版本必须大于 0。");
        if (value.DerivedFeatures.Count > 100)
            throw new ProcessResearchRuleException("单个优化特征集最多包含 100 个派生特征。");

        var availableInputs = controlVariableCodes.ToHashSet(StringComparer.Ordinal);
        var normalized = new List<ResearchDerivedFeature>(value.DerivedFeatures.Count);
        foreach (var feature in value.DerivedFeatures)
        {
            var name = NormalizeCode(feature.Name, "派生特征名称");
            if (!availableInputs.Add(name))
                throw new ProcessResearchRuleException($"派生特征名称重复或与控制变量冲突：{name}。");
            var featureOperator = feature.Operator.Trim().ToLowerInvariant();
            if (!ResearchDerivedFeatureOperators.IsValid(featureOperator))
                throw new ProcessResearchRuleException($"派生特征 {name} 的运算符无效。");
            var inputs = feature.Inputs.Select(input =>
                NormalizeCode(input, $"派生特征 {name} 的输入")).ToArray();
            if (inputs.Length == 0)
                throw new ProcessResearchRuleException($"派生特征 {name} 至少需要一个输入。");
            var exactArity = featureOperator switch
            {
                ResearchDerivedFeatureOperators.Identity or
                    ResearchDerivedFeatureOperators.Absolute => 1,
                ResearchDerivedFeatureOperators.Difference or
                    ResearchDerivedFeatureOperators.AbsoluteDifference or
                    ResearchDerivedFeatureOperators.Ratio => 2,
                _ => 0
            };
            if (exactArity > 0 && inputs.Length != exactArity)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 的运算符 {featureOperator} 必须恰好有 {exactArity} 个输入。");
            }
            var unavailable = inputs.FirstOrDefault(input =>
                !availableInputs.Contains(input) || string.Equals(input, name, StringComparison.Ordinal));
            if (unavailable is not null)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 引用了未知或尚未定义的输入 {unavailable}。");
            }
            if (!double.IsFinite(feature.NormalizationOffset) ||
                !double.IsFinite(feature.NormalizationScale) ||
                feature.NormalizationScale <= 0 ||
                !double.IsFinite(feature.Epsilon) ||
                feature.Epsilon <= 0)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 的归一化参数或 epsilon 无效。");
            }
            normalized.Add(feature with
            {
                Name = name,
                Operator = featureOperator,
                Inputs = inputs
            });
        }

        return value with
        {
            FeatureSetId = NormalizeCode(value.FeatureSetId, "优化特征集标识"),
            DerivedFeatures = normalized
        };
    }

    private static ResearchObjective NormalizeObjective(ResearchObjective value)
    {
        if (!double.IsFinite(value.Target) || !double.IsFinite(value.Weight) || value.Weight <= 0 ||
            value.Baseline is { } baseline && !double.IsFinite(baseline) ||
            value.LowerLimit is { } lower && !double.IsFinite(lower) ||
            value.UpperLimit is { } upper && !double.IsFinite(upper) ||
            value.LowerLimit is { } min && value.UpperLimit is { } max && min >= max)
            throw new ProcessResearchRuleException("研发目标的数值范围无效。");
        var direction = value.Direction.Trim().ToLowerInvariant();
        if (direction is not ("maximize" or "minimize" or "target" or "range"))
            throw new ProcessResearchRuleException("研发目标方向必须是 maximize、minimize、target 或 range。");
        return value with
        {
            Code = NormalizeCode(value.Code, "研发目标代码"),
            Name = RequiredText(value.Name, "研发目标名称", 240),
            Unit = RequiredText(value.Unit, "研发目标单位", 40),
            Direction = direction,
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static ResearchVariable NormalizeVariable(ResearchVariable value)
    {
        if (!ResearchVariableRoles.IsValid(value.Role))
            throw new ProcessResearchRuleException("工艺变量角色无效。");
        if (value.LowerLimit is { } lower && !double.IsFinite(lower) ||
            value.UpperLimit is { } upper && !double.IsFinite(upper) ||
            value.LowerLimit is { } min && value.UpperLimit is { } max && min >= max)
            throw new ProcessResearchRuleException("工艺变量范围无效。");
        return value with
        {
            Code = NormalizeCode(value.Code, "工艺变量代码"),
            Name = RequiredText(value.Name, "工艺变量名称", 240),
            Role = value.Role.Trim().ToLowerInvariant(),
            Unit = RequiredText(value.Unit, "工艺变量单位", 40),
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static IReadOnlyList<EvidenceReference> NormalizeEvidence(
        Guid projectId,
        IReadOnlyList<EvidenceReference> source)
        => source.Select(value =>
        {
            var kind = RequiredText(value.Kind, "证据类型", 80).ToLowerInvariant();
            var hash = RequiredText(value.ContentHash, "证据内容摘要", 64).ToLowerInvariant();
            if (!EvidenceKinds.IsValid(kind))
                throw new ProcessResearchRuleException("证据类型必须是系统定义的可验证类型。");
            if (!HashPattern().IsMatch(hash))
                throw new ProcessResearchRuleException("证据内容摘要必须是 64 位 SHA-256。");
            if (value.ProjectId != Guid.Empty && value.ProjectId != projectId)
                throw new ProcessResearchRuleException("证据不属于当前研发项目。");
            return value with
            {
                EvidenceId = value.EvidenceId == Guid.Empty ? Guid.CreateVersion7() : value.EvidenceId,
                ProjectId = projectId,
                Kind = kind,
                ReferenceId = RequiredText(value.ReferenceId, "证据标识", 500),
                Summary = RequiredText(value.Summary, "证据摘要", 2000),
                ContentHash = hash,
                CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt
            };
        }).ToArray();

    private static EvidenceReference CreateEvidence(
        Guid projectId,
        string kind,
        string referenceId,
        string summary,
        string contentHash,
        DateTimeOffset createdAt)
        => new()
        {
            EvidenceId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Kind = kind,
            ReferenceId = referenceId,
            Summary = summary,
            ContentHash = contentHash.ToLowerInvariant(),
            CreatedAt = createdAt
        };

    private async Task AuditAsync(
        Guid projectId,
        string resourceType,
        string resourceId,
        string action,
        string userId,
        string? fromStatus,
        string? toStatus,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = projectId,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = NormalizeUser(userId),
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static IReadOnlyList<string> NormalizeCodes(
        IReadOnlyList<string> source,
        string field)
        => source.Select(value => NormalizeCode(value, field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> NormalizeTextList(
        IReadOnlyList<string> source,
        string field,
        int maximumLength)
        => source.Select(value => RequiredText(value, field, maximumLength))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private async Task UpdateHypothesisAfterResultAsync(
        ResearchExperiment experiment,
        ResearchExperimentResult result,
        string userId,
        CancellationToken ct)
    {
        if (experiment.HypothesisId is not { } hypothesisId)
            return;
        var hypothesis = await store.GetHypothesisAsync(hypothesisId, ct).ConfigureAwait(false);
        if (hypothesis is null || hypothesis.ProjectId != experiment.ProjectId)
            return;
        if (hypothesis.ValidationOutcomeCode is null ||
            hypothesis.ExpectedEffectDirection is null || hypothesis.MinimumEffect is null)
            return;

        var now = DateTimeOffset.UtcNow;
        var evidence = CreateEvidence(
            experiment.ProjectId,
            EvidenceKinds.ExperimentResult,
            result.ResultId.ToString(),
            "用于验证研发假设的实验结果。",
            result.AnalysisHash,
            now);
        var validationEvidence = hypothesis.ValidationEvidence
            .Append(evidence)
            .GroupBy(static value => (value.Kind, value.ReferenceId))
            .Select(static group => group.First())
            .ToArray();
        var status = EvaluateHypothesis(hypothesis, experiment, result);
        var supporting = status is ResearchHypothesisStatuses.Supported or
            ResearchHypothesisStatuses.Validated
            ? hypothesis.SupportingEvidence.Append(evidence)
                .GroupBy(static value => (value.Kind, value.ReferenceId))
                .Select(static group => group.First()).ToArray()
            : hypothesis.SupportingEvidence;
        var opposing = status == ResearchHypothesisStatuses.Rejected
            ? hypothesis.OpposingEvidence.Append(evidence)
                .GroupBy(static value => (value.Kind, value.ReferenceId))
                .Select(static group => group.First()).ToArray()
            : hypothesis.OpposingEvidence;
        var saved = await store.SaveHypothesisAsync(
            hypothesis with
            {
                Status = status,
                SupportingEvidence = supporting,
                OpposingEvidence = opposing,
                ValidationEvidence = validationEvidence,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(
            experiment.ProjectId,
            "hypothesis",
            saved.HypothesisId.ToString(),
            "validation-result-recorded",
            userId,
            hypothesis.Status,
            saved.Status,
            ct).ConfigureAwait(false);
    }

    private static string EvaluateHypothesis(
        ResearchHypothesis hypothesis,
        ResearchExperiment experiment,
        ResearchExperimentResult result)
    {
        if (!result.SafetyPassed || hypothesis.ValidationOutcomeCode is null ||
            hypothesis.ExpectedEffectDirection is null || hypothesis.MinimumEffect is null)
            return ResearchHypothesisStatuses.Inconclusive;
        var metric = result.Metrics.FirstOrDefault(value =>
            string.Equals(value.ObjectiveCode, hypothesis.ValidationOutcomeCode,
                StringComparison.Ordinal));
        if (metric is null || metric.LowerConfidenceBound is null ||
            metric.UpperConfidenceBound is null)
            return ResearchHypothesisStatuses.Inconclusive;
        var minimumEffect = hypothesis.MinimumEffect.Value;
        var directionalResult = hypothesis.ExpectedEffectDirection switch
        {
            ResearchHypothesisEffectDirections.Increase
                when metric.LowerConfidenceBound >= minimumEffect =>
                ResearchHypothesisStatuses.Supported,
            ResearchHypothesisEffectDirections.Increase
                when metric.UpperConfidenceBound <= -minimumEffect =>
                ResearchHypothesisStatuses.Rejected,
            ResearchHypothesisEffectDirections.Decrease
                when metric.UpperConfidenceBound <= -minimumEffect =>
                ResearchHypothesisStatuses.Supported,
            ResearchHypothesisEffectDirections.Decrease
                when metric.LowerConfidenceBound >= minimumEffect =>
                ResearchHypothesisStatuses.Rejected,
            _ => ResearchHypothesisStatuses.Inconclusive
        };
        if (directionalResult != ResearchHypothesisStatuses.Supported)
            return directionalResult;
        var isRepeatedIntervention =
            experiment.DesignMethod == ResearchDesignMethods.BayesianOptimization &&
            experiment.Optimization?.Intent == ResearchOptimizationIntents.ValidateHypothesis &&
            experiment.Optimization.ReplicatesPerCondition >= 2 &&
            experiment.Optimization.BlockCount >= 2 &&
            result.RunCount >= experiment.Optimization.DistinctConditionCount * 2 &&
            result.DistinctBlockCount >= 2;
        return isRepeatedIntervention
            ? ResearchHypothesisStatuses.Validated
            : ResearchHypothesisStatuses.Supported;
    }

    private static bool IsInsideWindow(
        ResearchProcessWindow window,
        ExperimentRunObservation observation)
    {
        var factors = observation.ActualFactors.ToDictionary(
            static value => value.VariableCode,
            static value => value.Value,
            StringComparer.Ordinal);
        return window.Variables.All(variable =>
            factors.TryGetValue(variable.VariableCode, out var value) &&
            value >= variable.LowerBound &&
            value <= variable.UpperBound);
    }

    private static int CountDistinctContext(
        IReadOnlyList<ExperimentRunObservation> observations,
        params string[] keys)
        => observations
            .Select(observation => keys
                .Select(key => observation.Context.GetValueOrDefault(key))
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static bool MeetsMeasuredSpecification(
        ResearchProject project,
        ExperimentRunObservation observation)
        => project.Objectives.All(objective =>
               observation.Outcomes.TryGetValue(objective.Code, out var value) &&
               MeetsObjective(objective, value)) &&
           project.OutcomeConstraints.All(constraint =>
               observation.ConstraintOutcomes.TryGetValue(constraint.Code, out var value) &&
               (constraint.Operator == "<=" ? value <= constraint.Limit : value >= constraint.Limit));

    private static bool MeetsObjective(ResearchObjective objective, double value)
        => objective.Direction switch
        {
            "minimize" => value <= (objective.UpperLimit ?? objective.Target),
            "maximize" => value >= (objective.LowerLimit ?? objective.Target),
            "range" or "target"
                when objective.LowerLimit is { } min && objective.UpperLimit is { } max =>
                value >= min && value <= max,
            // 只有目标点而没有公差时，系统无法诚实判断“达到规格”。
            "target" => false,
            _ => false
        };

    private static double WilsonLowerBound(int successCount, int totalCount)
    {
        if (totalCount <= 0)
            return 0.01;
        const double z = 1.96;
        var proportion = (double)successCount / totalCount;
        var denominator = 1 + z * z / totalCount;
        var centre = proportion + z * z / (2 * totalCount);
        var margin = z * Math.Sqrt(
            proportion * (1 - proportion) / totalCount +
            z * z / (4 * totalCount * totalCount));
        return Math.Max(0.01, (centre - margin) / denominator);
    }

    private static ResearchExperimentExecution UpdateExecution(
        ResearchExperiment experiment,
        string targetStatus,
        string actor)
    {
        var execution = experiment.Execution ?? BuildExecution(experiment);
        return targetStatus switch
        {
            ResearchExperimentStatuses.Approved => execution with
            {
                State = ResearchExperimentExecutionStates.Ready
            },
            ResearchExperimentStatuses.Running => execution with
            {
                State = ResearchExperimentExecutionStates.Dispatched,
                DispatchedBy = actor,
                DispatchedAt = DateTimeOffset.UtcNow
            },
            ResearchExperimentStatuses.Completed => execution with
            {
                State = ResearchExperimentExecutionStates.Completed,
                CompletedAt = DateTimeOffset.UtcNow
            },
            ResearchExperimentStatuses.Cancelled => execution with
            {
                State = ResearchExperimentExecutionStates.Cancelled
            },
            _ => execution
        };
    }

    private static ResearchExperimentExecution BuildExecution(ResearchExperiment experiment)
        => new()
        {
            DispatchId = Guid.CreateVersion7(),
            Commands = experiment.RunPlan.Select(run => new ExperimentExecutionCommand
            {
                CommandId = Guid.CreateVersion7(),
                RunKey = run.RunKey,
                Sequence = run.Sequence,
                BlockKey = run.BlockKey,
                ReplicateKey = run.ReplicateKey,
                RequestedFactors = run.Factors
            }).ToArray()
        };

    private static string NormalizeCode(string? value, string field)
    {
        var result = RequiredText(value, field, 120).ToLowerInvariant();
        if (!CodePattern().IsMatch(result))
            throw new ProcessResearchRuleException(
                $"{field}必须以字母开头，并且只包含小写字母、数字、点、下划线或连字符。");
        return result;
    }

    private static string NormalizeUser(string? value)
        => RequiredText(value, "用户标识", 240).ToLowerInvariant();

    private static string NormalizeStatus(
        string? value,
        Func<string?, bool> validator,
        string field)
    {
        var result = RequiredText(value, field, 80).ToLowerInvariant();
        if (!validator(result))
            throw new ProcessResearchRuleException($"{field}无效。");
        return result;
    }

    private static string RequiredText(string? value, string field, int maximumLength)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且最长 {maximumLength} 个字符。");
        return result;
    }

    private static string? OptionalText(string? value, int maximumLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result))
            return null;
        if (result.Length > maximumLength)
            throw new ProcessResearchRuleException($"文本最长 {maximumLength} 个字符。");
        return result;
    }

    [GeneratedRegex("^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
