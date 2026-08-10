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
        var controlledOnline = request.Optimization?.Mode == ResearchOptimizationModes.Controlled;
        if (controlledOnline)
        {
            if (request.RunPlan.Count != 1)
                throw new ProcessResearchRuleException("受控在线实验必须且只能包含一条运行建议。");
            if (onlineAdmission is null)
                throw new ProcessResearchRuleException("受控在线准入服务不可用，按失败关闭处理。");
            var admission = await onlineAdmission.RequireAsync(projectId, ct).ConfigureAwait(false);
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
            await onlineAdmission.RequireAsync(experiment.ProjectId, ct).ConfigureAwait(false);
        }
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

        ExperimentFactorSetting NormalizeApproved(ExperimentFactorSetting factor)
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

        IReadOnlyList<ExperimentFactorSetting> approved;
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
}
