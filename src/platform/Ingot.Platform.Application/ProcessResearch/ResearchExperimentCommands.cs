// 校验实验设计、冻结输入并编排受约束的研发实验状态变更。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>编排冻结约束下的研发实验设计和状态变更。</summary>
public sealed partial class ResearchExperimentCommands(
    IResearchExperimentCommandStore store,
    IResearchOnlineAdmissionGate? onlineAdmission = null,
    IResearchExperimentPlanValidator? experimentValidation = null,
    IResearchExperimentKnowledgeGate? knowledgeGate = null)
{
    public async Task<ResearchExperiment> CreateExperimentAsync(
        Guid projectId,
        ResearchExperiment request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        var executionCategory = request.Optimization?.Mode switch
        {
            ResearchOptimizationModes.Shadow => ResearchExperimentExecutionCategories.Shadow,
            ResearchOptimizationModes.Controlled => ResearchExperimentExecutionCategories.ControlledOnline,
            _ => ResearchExperimentExecutionCategories.Offline
        };
        var safety = await ApplySafetyTemplateAsync(project, request, executionCategory, ct)
            .ConfigureAwait(false);
        request = safety.Request;
        if (experimentValidation is not null)
        {
            var validation = await experimentValidation.ValidateAsync(projectId, request, ct)
                .ConfigureAwait(false);
            if (!validation.IsValid)
                throw new ResearchExperimentValidationException(validation.Errors);
        }
        ResearchOperatingRegion? validationWindow = null;
        if (request.ValidationOperatingRegionId is { } validationOperatingRegionId)
        {
            validationWindow = await store.GetOperatingRegionAsync(validationOperatingRegionId, ct)
                .ConfigureAwait(false);
            if (validationWindow is null ||
                validationWindow.ProjectId != projectId ||
                validationWindow.Status != OperatingRegionStatuses.Candidate)
                throw new ProcessResearchRuleException("独立验证实验必须引用当前项目中的候选工艺操作域。");
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
        var controlledOnline = request.Optimization?.Mode == ResearchOptimizationModes.Controlled;
        if (controlledOnline)
        {
            if (request.RunPlan.Count != 1)
                throw new ProcessResearchRuleException("受控在线实验必须且只能包含一条运行建议。");
            if (onlineAdmission is null)
                throw new ProcessResearchRuleException("受控在线准入服务不可用，按失败关闭处理。");
            var admission = await onlineAdmission.RequireAsync(
                projectId,
                request.Optimization!.MechanismKnowledgeSnapshotHash,
                ct).ConfigureAwait(false);
            if (request.Optimization?.OnlineAdmission is not { Eligible: true } frozenAdmission ||
                frozenAdmission.HistoricalReplayReportId != admission.HistoricalReplayReportId ||
                !string.Equals(frozenAdmission.HistoricalReplayReportHash,
                    admission.HistoricalReplayReportHash, StringComparison.Ordinal) ||
                !string.Equals(frozenAdmission.ShadowReportHash,
                    admission.ShadowReportHash, StringComparison.Ordinal) ||
                frozenAdmission.RollbackDrillId != admission.RollbackDrillId ||
                !string.Equals(frozenAdmission.RollbackDrillRecordHash,
                    admission.RollbackDrillRecordHash, StringComparison.Ordinal))
                throw new ProcessResearchRuleException("受控在线建议没有冻结当前有效的回放与影子准入证据。");
        }
        else if (request.RunPlan.Count < (validationWindow is null ? 2 : 3))
            throw new ProcessResearchRuleException(
                validationWindow is null
                    ? "实验计划必须至少包含两个运行条件，不能用单点设置代替实验设计。"
                    : "独立验证实验至少需要三个重复运行。");

        ResearchVariableSetting NormalizeFactor(ResearchVariableSetting value)
        {
            var code = NormalizeCode(value.VariableCode, "实验变量");
            if (!knownVariables.TryGetValue(code, out var variable))
                throw new ProcessResearchRuleException($"实验变量 {code} 不是项目中的可控变量。");
            var unit = RequiredText(value.Unit, "实验变量单位", 40);
            var normalizedValue = value.Value;
            if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase) &&
                !ProcessUnitConverter.TryConvert(value.Value, unit, variable.Unit, out normalizedValue))
                throw new ProcessResearchRuleException(
                    $"实验变量 {code} 的单位必须与项目变量一致或可转换为 {variable.Unit}。 ");
            if (!double.IsFinite(normalizedValue) ||
                variable.LowerLimit is { } lower && normalizedValue < lower ||
                variable.UpperLimit is { } upper && normalizedValue > upper)
                throw new ProcessResearchRuleException($"实验变量 {code} 超出允许范围。");
            return value with { VariableCode = code, Value = normalizedValue, Unit = variable.Unit };
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
                ExecutionKey = RequiredText(run.ExecutionKey, "实验运行标识", 120),
                Sequence = run.Sequence > 0 ? run.Sequence : index + 1,
                BlockKey = OptionalText(run.BlockKey, 120),
                ReplicateKey = OptionalText(run.ReplicateKey, 120),
                Factors = factors
            };
        }).ToArray();
        if (runPlan.Select(static value => value.ExecutionKey).Distinct(StringComparer.Ordinal).Count() !=
            runPlan.Length ||
            runPlan.Select(static value => value.Sequence).Distinct().Count() != runPlan.Length)
            throw new ProcessResearchRuleException("实验运行标识和执行顺序必须唯一。");
        if (designMethod != ResearchDesignMethods.HistoricalObservation)
        {
            foreach (var run in runPlan)
                ValidateHardBoundaries(project, run.Factors, $"实验运行 {run.ExecutionKey}");
        }
        var baselineExecutionKeys = request.BaselineExecutionKeys
            .Select(value => RequiredText(value, "对照运行标识", 120))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (baselineExecutionKeys.Length != request.BaselineExecutionKeys.Count)
            throw new ProcessResearchRuleException("对照运行标识不能重复。");
        if (baselineExecutionKeys.Length == 1)
            throw new ProcessResearchRuleException("生成独立对照置信区间至少需要两个对照运行。");
        if (baselineExecutionKeys.Length > 0)
        {
            var currentExecutionKeys = runPlan.Select(static value => value.ExecutionKey)
                .ToHashSet(StringComparer.Ordinal);
            if (currentExecutionKeys.All(baselineExecutionKeys.Contains))
                throw new ProcessResearchRuleException("实验必须至少保留一个非对照运行用于效果比较。");
            var eligiblePriorExecutionKeys = (await store.ListExperimentsAsync(projectId, ct)
                    .ConfigureAwait(false))
                .Where(static value =>
                    value.DesignMethod == ResearchDesignMethods.HistoricalObservation ||
                    value.Status == ResearchExperimentStatuses.Completed)
                .SelectMany(static value => value.RunPlan)
                .Select(static value => value.ExecutionKey)
                .ToHashSet(StringComparer.Ordinal);
            if (baselineExecutionKeys.Any(key =>
                    !currentExecutionKeys.Contains(key) && !eligiblePriorExecutionKeys.Contains(key)))
                throw new ProcessResearchRuleException(
                    "对照运行必须来自本实验、已导入的历史观察或已完成实验。");
        }
        var distinctConditions = runPlan
            .Select(run => string.Join("|", run.Factors
                .OrderBy(static factor => factor.VariableCode)
                .Select(static factor => $"{factor.VariableCode}:{factor.Value:R}")))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctConditions < 2 && validationWindow is null && !controlledOnline)
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
                !ResearchOptimizationModes.IsValid(request.Optimization.Mode) ||
                !request.Optimization.RunPredictions.Select(static value => value.ExecutionKey)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(runPlan.Select(static value => value.ExecutionKey)))
                throw new ProcessResearchRuleException("优化实验的模型版本、输入摘要或运行预测无效。");
            await ValidateCurrentMechanismKnowledgeAsync(request with { ProjectId = projectId }, ct)
                .ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var value = request with
        {
            ExperimentId = request.ExperimentId == Guid.Empty
                ? Guid.CreateVersion7()
                : request.ExperimentId,
            ProjectId = projectId,
            ValidationOperatingRegionId = validationWindow?.OperatingRegionId,
            Name = RequiredText(request.Name, "实验名称", 240),
            DesignMethod = designMethod,
            ExecutionCategory = executionCategory,
            SafetyTemplateSource = safety.Source,
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
            BaselineExecutionKeys = baselineExecutionKeys,
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
                    ExecutionKey = run.ExecutionKey,
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
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (await store.GetExperimentAsync(value.ExperimentId, ct).ConfigureAwait(false) is not null)
            throw new ProcessResearchRuleException("实验标识已经存在。");
        var saved = await store.SaveExperimentTransactionAsync(
            value,
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = projectId,
                ResourceType = "experiment",
                ResourceId = value.ExperimentId.ToString(),
                Action = "planned",
                FromStatus = null,
                ToStatus = value.Status,
                UserId = NormalizeUser(userId),
                CreatedAt = now
            },
            ct).ConfigureAwait(false);
        return saved;
    }

    private async Task<(ResearchExperiment Request, string? Source)> ApplySafetyTemplateAsync(
        ResearchProject project,
        ResearchExperiment request,
        string executionCategory,
        CancellationToken ct)
    {

        if (executionCategory == ResearchExperimentExecutionCategories.ControlledOnline ||
            (!string.IsNullOrWhiteSpace(request.StopRule) && !string.IsNullOrWhiteSpace(request.RollbackPlan)))
            return (request, null);
        var template = project.SafetyTemplates.FirstOrDefault(item =>
            item.ExecutionCategory == executionCategory);
        if (template is not null)
        {
            return (request with
            {
                StopRule = string.IsNullOrWhiteSpace(request.StopRule) ? template.StopRule : request.StopRule,
                RollbackPlan = string.IsNullOrWhiteSpace(request.RollbackPlan) ? template.RollbackPlan : request.RollbackPlan
            }, $"project:{executionCategory}");
        }
        var prior = (await store.ListExperimentsAsync(project.ProjectId, ct).ConfigureAwait(false))
            .Where(item => item.ExecutionCategory == executionCategory)
            .Where(item => !string.IsNullOrWhiteSpace(item.StopRule) && !string.IsNullOrWhiteSpace(item.RollbackPlan))
            .OrderByDescending(static item => item.UpdatedAt)
            .FirstOrDefault();
        if (prior is null) return (request, null);
        return (request with
        {
            StopRule = string.IsNullOrWhiteSpace(request.StopRule) ? prior.StopRule : request.StopRule,
            RollbackPlan = string.IsNullOrWhiteSpace(request.RollbackPlan) ? prior.RollbackPlan : request.RollbackPlan
        }, $"experiment:{prior.ExperimentId}");
    }

    public async Task<ResearchExperiment> CloneExperimentAsync(
        Guid experimentId,
        ResearchExperimentCloneRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var source = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实验不存在。");
        if (source.DesignMethod == ResearchDesignMethods.BayesianOptimization)
            throw new ProcessResearchRuleException("贝叶斯优化实验必须基于当前观察重新生成，不能直接复制。 ");
        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var keyMap = source.RunPlan.ToDictionary(
            static run => run.ExecutionKey,
            run => $"copy-{suffix}-{run.Sequence:D2}",
            StringComparer.Ordinal);
        var clonedRuns = source.RunPlan.Select(run => run with
        {
            ExecutionKey = keyMap[run.ExecutionKey]
        }).ToArray();
        var baseline = source.BaselineExecutionKeys.Select(key =>
            keyMap.TryGetValue(key, out var replacement) ? replacement : key).ToArray();
        return await CreateExperimentAsync(source.ProjectId, source with
        {
            ExperimentId = Guid.Empty,
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"{source.Name}（副本）"
                : request.Name.Trim(),
            Status = ResearchExperimentStatuses.Planned,
            RunPlan = clonedRuns,
            BaselineExecutionKeys = baseline,
            ResultIds = [],
            Optimization = null,
            ControlledDecision = null,
            Execution = null,
            ApprovedBy = null,
            ApprovedAt = null,
            CreatedBy = "",
            CreatedAt = default,
            UpdatedAt = default,
            Revision = 1
        }, userId, ct).ConfigureAwait(false);
    }

    public async Task<ResearchExperiment> ChangeExperimentStatusAsync(
        Guid experimentId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实验不存在。");
        var project = await RequireMutableProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false);
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
        if (targetStatus == ResearchExperimentStatuses.Approved &&
            experiment.Optimization?.Mode == ResearchOptimizationModes.Shadow)
            throw new ProcessResearchRuleException("影子建议只用于旁路评估，不能批准或下发设备。");
        if (targetStatus == ResearchExperimentStatuses.Approved &&
            experiment.Optimization?.Mode == ResearchOptimizationModes.Controlled)
        {
            if (experiment.ControlledDecision?.Decision is not
                (ResearchControlledDecisionStatuses.Accepted or ResearchControlledDecisionStatuses.Modified))
                throw new ProcessResearchRuleException("受控在线建议必须先由工程师明确接受或修改，才能批准。");
            if (onlineAdmission is null)
                throw new ProcessResearchRuleException("受控在线准入服务不可用，按失败关闭处理。");
            await onlineAdmission.RequireAsync(
                experiment.ProjectId,
                experiment.Optimization.MechanismKnowledgeSnapshotHash,
                ct).ConfigureAwait(false);
        }
        if (targetStatus is ResearchExperimentStatuses.Approved or ResearchExperimentStatuses.Running)
        {
            if (experiment.DesignMethod == ResearchDesignMethods.HistoricalObservation)
                throw new ProcessResearchRuleException("历史观察只用于只读证据，不能批准或下发执行。");
            foreach (var run in experiment.RunPlan)
                ValidateHardBoundaries(project, run.Factors, $"实验运行 {run.ExecutionKey}");
        }
        if (targetStatus is ResearchExperimentStatuses.Approved or ResearchExperimentStatuses.Running)
            await ValidateCurrentMechanismKnowledgeAsync(experiment, ct).ConfigureAwait(false);
        if (targetStatus == ResearchExperimentStatuses.Running &&
            experiment.ProjectRevision != project.Revision)
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

        var now = DateTimeOffset.UtcNow;
        var updated = experiment with
        {
            Revision = experiment.Revision + 1,
            Status = targetStatus,
            Execution = UpdateExecution(experiment, targetStatus, actor),
            ApprovedBy = targetStatus == ResearchExperimentStatuses.Approved
                    ? actor
                    : experiment.ApprovedBy,
            ApprovedAt = targetStatus == ResearchExperimentStatuses.Approved
                    ? now
                    : experiment.ApprovedAt,
            UpdatedAt = now
        };
        var saved = await store.SaveExperimentTransactionAsync(
            updated,
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = experiment.ProjectId,
                ResourceType = "experiment",
                ResourceId = experimentId.ToString(),
                Action = "status-changed",
                FromStatus = experiment.Status,
                ToStatus = targetStatus,
                UserId = actor,
                CreatedAt = now
            },
            ct).ConfigureAwait(false);
        return saved;
    }

    private async Task ValidateCurrentMechanismKnowledgeAsync(
        ResearchExperiment experiment,
        CancellationToken ct)
    {
        if (experiment.Optimization is not null && knowledgeGate is not null)
            await knowledgeGate.ValidateAsync(experiment, ct).ConfigureAwait(false);
    }

    public async Task<ResearchExperiment> DecideControlledExperimentAsync(
        Guid experimentId,
        ResearchControlledDecisionRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实验不存在。");
        var project = await RequireMutableProjectAsync(experiment.ProjectId, ct).ConfigureAwait(false);
        if (experiment.Optimization?.Mode != ResearchOptimizationModes.Controlled ||
            experiment.RunPlan.Count != 1)
            throw new ProcessResearchRuleException("只有单条受控在线建议可以记录人工决策。");
        if (experiment.Status != ResearchExperimentStatuses.Planned)
            throw new ProcessResearchRuleException("只有尚未批准的受控在线建议可以决策。");
        if (experiment.ControlledDecision is not null)
            return experiment;
        var actor = NormalizeUser(userId);
        if (string.Equals(experiment.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("受控建议生成者不能替代现场工程师作出执行决策。");
        var decisionStatus = NormalizeStatus(
            request.Decision, ResearchControlledDecisionStatuses.IsValid, "受控在线决策");
        var reason = OptionalText(request.Reason, 4000);
        var suggested = experiment.RunPlan[0].Factors
            .OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray();
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);

        ResearchVariableSetting NormalizeApproved(ResearchVariableSetting factor)
        {
            var code = NormalizeCode(factor.VariableCode, "批准变量");
            if (!controls.TryGetValue(code, out var variable) ||
                !double.IsFinite(factor.Value) ||
                variable.LowerLimit is { } lower && factor.Value < lower ||
                variable.UpperLimit is { } upper && factor.Value > upper)
                throw new ProcessResearchRuleException($"批准变量 {code} 超出项目硬边界。");
            var unit = RequiredText(factor.Unit, "批准变量单位", 40);
            if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"批准变量 {code} 的单位必须与项目定义一致。");
            return factor with { VariableCode = code, Unit = variable.Unit };
        }

        IReadOnlyList<ResearchVariableSetting> approved;
        if (decisionStatus == ResearchControlledDecisionStatuses.Rejected)
        {
            if (reason is null)
                throw new ProcessResearchRuleException("拒绝受控在线建议必须记录原因，以便转化为约束或适用范围。");
            approved = [];
        }
        else
        {
            approved = (request.ApprovedFactors.Count == 0 &&
                        decisionStatus == ResearchControlledDecisionStatuses.Accepted
                    ? suggested
                    : request.ApprovedFactors.Select(NormalizeApproved)
                        .OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray());
            if (approved.Count != controls.Count ||
                !approved.Select(static value => value.VariableCode)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
                throw new ProcessResearchRuleException("工程师批准值必须包含且仅包含全部可控变量。");
            var changed = approved.Zip(suggested).Any(pair =>
                pair.First.VariableCode != pair.Second.VariableCode ||
                Math.Abs(pair.First.Value - pair.Second.Value) > 1e-12);
            if (decisionStatus == ResearchControlledDecisionStatuses.Accepted && changed)
                throw new ProcessResearchRuleException("接受建议时批准值必须等于模型建议；需要改值请使用 modified。");
            if (decisionStatus == ResearchControlledDecisionStatuses.Modified && (!changed || reason is null))
                throw new ProcessResearchRuleException("修改建议必须提供不同的完整批准值并说明原因。");
            ValidateHardBoundaries(project, approved, "工程师批准值");
        }

        var now = DateTimeOffset.UtcNow;
        var decisionHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                experiment.ExperimentId,
                Decision = decisionStatus,
                Suggested = suggested,
                Approved = approved,
                Reason = reason,
                Actor = actor,
                DecidedAt = now
            })));
        var decision = new ResearchControlledDecision
        {
            Decision = decisionStatus,
            SuggestedFactors = suggested,
            ApprovedFactors = approved,
            Reason = reason,
            DecisionSnapshotHash = decisionHash,
            DecidedBy = actor,
            DecidedAt = now
        };
        var execution = experiment.Execution ?? BuildExecution(experiment);
        var updated = experiment with
        {
            ControlledDecision = decision,
            Factors = approved.Count > 0 ? approved : experiment.Factors,
            RunPlan = approved.Count > 0
                    ? [experiment.RunPlan[0] with { Factors = approved }]
                    : experiment.RunPlan,
            Status = decisionStatus == ResearchControlledDecisionStatuses.Rejected
                    ? ResearchExperimentStatuses.Cancelled
                    : experiment.Status,
            Execution = decisionStatus == ResearchControlledDecisionStatuses.Rejected
                    ? execution with { State = ResearchExperimentExecutionStates.Cancelled }
                    : execution with
                    {
                        Commands =
                        [
                            execution.Commands[0] with { RequestedFactors = approved }
                        ]
                    },
            Revision = experiment.Revision + 1,
            UpdatedAt = now
        };
        var saved = await store.SaveControlledDecisionTransactionAsync(
            updated,
            new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ProjectId = experiment.ProjectId,
                ResourceType = "controlled-online-decision",
                ResourceId = experiment.ExperimentId.ToString(),
                Action = $"controlled-{decisionStatus}",
                FromStatus = experiment.Status,
                ToStatus = updated.Status,
                UserId = actor,
                CreatedAt = now
            },
            ct).ConfigureAwait(false);
        return saved;
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

    private static IReadOnlyList<string> NormalizeCodes(
        IReadOnlyList<string> source,
        string field)
        => source.Select(value => NormalizeCode(value, field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
            throw new ProcessResearchRuleException(
                $"{field}不能为空且最长 {maximumLength} 个字符。");
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

    private static void ValidateHardBoundaries(
        ResearchProject project,
        IReadOnlyList<ResearchVariableSetting> factors,
        string label)
    {
        var values = factors.ToDictionary(
            static factor => factor.VariableCode,
            StringComparer.Ordinal);
        foreach (var constraint in project.Constraints)
        {
            if (!values.TryGetValue(constraint.VariableCode, out var factor))
                throw new ProcessResearchRuleException(
                    $"{label}缺少安全约束变量 {constraint.VariableCode}。");
            var limit = constraint.Limit;
            if (!string.Equals(constraint.Unit, factor.Unit, StringComparison.OrdinalIgnoreCase) &&
                !ProcessUnitConverter.TryConvert(constraint.Limit, constraint.Unit, factor.Unit, out limit))
            {
                throw new ProcessResearchRuleException(
                    $"安全约束 {constraint.Code} 的单位不能换算为 {factor.Unit}。");
            }
            var passed = constraint.Operator switch
            {
                "<=" => factor.Value <= limit,
                ">=" => factor.Value >= limit,
                _ => false
            };
            if (!passed)
                throw new ProcessResearchRuleException(
                    $"{label}违反已声明安全边界 {constraint.Code}。");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

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
                ExecutionKey = run.ExecutionKey,
                Sequence = run.Sequence,
                BlockKey = run.BlockKey,
                ReplicateKey = run.ReplicateKey,
                RequestedFactors = run.Factors
            }).ToArray()
        };
}
