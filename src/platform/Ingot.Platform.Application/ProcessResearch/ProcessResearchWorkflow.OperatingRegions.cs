// 管理候选工艺操作域从实验室验证到受控在线生产验证的证据门禁。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed partial class ProcessResearchWorkflow
{
    public async Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
        Guid projectId,
        ResearchOperatingRegion request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        var knownVariables = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (request.Variables.Count == 0)
            throw new ProcessResearchRuleException("工艺操作域必须包含至少一个可控变量范围。");
        var variables = request.Variables.Select(value =>
        {
            var code = NormalizeCode(value.VariableCode, "工艺操作域变量");
            if (!knownVariables.TryGetValue(code, out var variable))
                throw new ProcessResearchRuleException($"工艺操作域变量 {code} 不是项目中的可控变量。");
            if (!double.IsFinite(value.LowerBound) || !double.IsFinite(value.UpperBound) ||
                value.LowerBound > value.UpperBound ||
                variable.LowerLimit is { } lower && value.LowerBound < lower ||
                variable.UpperLimit is { } upper && value.UpperBound > upper)
                throw new ProcessResearchRuleException($"工艺操作域变量 {code} 的范围无效。");
            var unit = RequiredText(value.Unit, "工艺操作域变量单位", 40);
            if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"工艺操作域变量 {code} 的单位必须与项目变量一致。");
            return value with
            {
                VariableCode = code,
                Unit = unit
            };
        }).ToArray();
        if (variables.Select(static value => value.VariableCode).Distinct(StringComparer.Ordinal).Count() !=
            variables.Length)
            throw new ProcessResearchRuleException("工艺操作域中的变量不能重复。");
        if (request.SupportingExperimentIds.Count == 0)
            throw new ProcessResearchRuleException("候选工艺操作域必须关联验证实验。");
        foreach (var experimentId in request.SupportingExperimentIds.Distinct())
        {
            var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
            if (experiment is null || experiment.ProjectId != projectId ||
                experiment.Status != ResearchExperimentStatuses.Completed)
                throw new ProcessResearchRuleException("工艺操作域只能引用当前项目中已完成的实验。");
        }
        if (request.SupportingResultIds.Count == 0)
            throw new ProcessResearchRuleException("候选工艺操作域必须关联实验计算结果。");
        var supportingResults = new List<ResearchExperimentResult>();
        foreach (var resultId in request.SupportingResultIds.Distinct())
        {
            var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false);
            if (result is null || result.ProjectId != projectId ||
                !request.SupportingExperimentIds.Contains(result.ExperimentId) ||
                !result.CalculatedFromSource || !result.SafetyPassed)
                throw new ProcessResearchRuleException("工艺操作域只能引用当前项目中通过安全检查的源数据计算结果。");
            supportingResults.Add(result);
        }
        var objectiveCodes = NormalizeCodes(request.ObjectiveCodes, "工艺操作域目标");
        var knownObjectives = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Count == 0 || objectiveCodes.Any(code => !knownObjectives.Contains(code)))
            throw new ProcessResearchRuleException("工艺操作域必须引用项目中已经定义的目标。");
        var coveredObjectives = supportingResults.SelectMany(static value => value.Metrics)
            .Select(static value => value.ObjectiveCode)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Any(code => !coveredObjectives.Contains(code)))
            throw new ProcessResearchRuleException("工艺操作域的计算结果尚未覆盖全部目标。");
        if (request.Confidence is <= 0 or > 1 || !double.IsFinite(request.Confidence))
            throw new ProcessResearchRuleException("工艺操作域置信度必须大于 0 且不超过 1。");
        var confidenceMethod = RequiredText(request.ConfidenceMethod, "置信度计算方法", 120)
            .ToLowerInvariant();
        if (!ResearchConfidenceMethods.IsValid(confidenceMethod))
            throw new ProcessResearchRuleException("工艺操作域置信度计算方法无效。");
        if (request.AnalysisRunId == Guid.Empty || !HashPattern().IsMatch(request.AnalysisHash))
            throw new ProcessResearchRuleException("工艺操作域必须关联可追溯的分析运行和 SHA-256 摘要。");
        if (supportingResults.All(result =>
                result.AnalysisRunId != request.AnalysisRunId ||
                !string.Equals(
                    result.AnalysisHash,
                    request.AnalysisHash,
                    StringComparison.OrdinalIgnoreCase)))
            throw new ProcessResearchRuleException("工艺操作域的分析运行必须来自所关联的实验结果。");

        var now = DateTimeOffset.UtcNow;
        var existing = request.OperatingRegionId == Guid.Empty
            ? null
            : await store.GetOperatingRegionAsync(request.OperatingRegionId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("工艺操作域不属于当前项目。");
        if (existing?.Status == OperatingRegionStatuses.Validated)
            throw new ProcessResearchRuleException("经过验证的工艺操作域保持不可变。");

        var evidence = NormalizeEvidence(projectId, request.Evidence).ToList();
        foreach (var result in supportingResults)
        {
            evidence.Add(CreateEvidence(
                projectId,
                EvidenceKinds.ExperimentResult,
                result.ResultId.ToString(),
                "支持该工艺操作域的实验结果。",
                result.AnalysisHash,
                now));
        }
        evidence.Add(CreateEvidence(
            projectId,
            EvidenceKinds.AnalysisRun,
            request.AnalysisRunId.ToString(),
            "生成候选工艺操作域的分析运行。",
            request.AnalysisHash.ToLowerInvariant(),
            now));

        var saved = await store.SaveOperatingRegionAsync(
            request with
            {
                OperatingRegionId = existing?.OperatingRegionId ??
                           (request.OperatingRegionId == Guid.Empty ? Guid.CreateVersion7() : request.OperatingRegionId),
                ProjectId = projectId,
                Name = RequiredText(request.Name, "工艺操作域名称", 240),
                Status = OperatingRegionStatuses.Candidate,
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
                Applicability = RequiredText(request.Applicability, "工艺操作域适用范围", 8000),
                ValidationLevel = OperatingRegionValidationLevels.Evidence,
                ValidationNotes = null,
                ValidatedBy = null,
                ValidatedAt = null,
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "operating-region", saved.OperatingRegionId.ToString(),
            existing is null ? "candidate-created" : "candidate-updated",
            userId, existing?.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchExperiment> CreateOperatingRegionValidationExperimentAsync(
        Guid operatingRegionId,
        string userId,
        CancellationToken ct = default)
    {
        var window = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺操作域不存在。");
        var project = await RequireMutableProjectAsync(window.ProjectId, ct).ConfigureAwait(false);
        if (window.Status != OperatingRegionStatuses.Candidate)
            throw new ProcessResearchRuleException("只有候选工艺操作域可以设计独立验证实验。");
        if (project.Status != ResearchProjectStatuses.Validating)
            throw new ProcessResearchRuleException("项目进入验证阶段后才能设计独立验证实验。");
        if (window.Variables.Any(static value => value.LowerBound != value.UpperBound))
            throw new ProcessResearchRuleException(
                "当前自动验证只支持候选设置点；连续工艺操作域必须先完成覆盖边界和交互作用的扩展实验。");

        var experiments = await store.ListExperimentsAsync(project.ProjectId, ct)
            .ConfigureAwait(false);
        var existing = experiments
            .Where(value => value.ValidationOperatingRegionId == operatingRegionId &&
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
                ExecutionKey = $"validation-{shortId}-r{index:00}",
                Sequence = index,
                BlockKey = $"validation-block-{index:00}",
                ReplicateKey = "candidate-setting",
                Factors = factors
            })
            .ToArray();
        return await ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                ExperimentId = experimentId,
                ValidationOperatingRegionId = operatingRegionId,
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
                RollbackPlan = "停止验证并恢复验证前已批准的生产工艺规范；候选操作域保持未验证状态。"
            },
            userId,
            ct).ConfigureAwait(false);
    }

    public async Task<ResearchOperatingRegion> AttachOperatingRegionValidationResultAsync(
        Guid operatingRegionId,
        ResearchExperiment experiment,
        ResearchExperimentResult result,
        string userId,
        CancellationToken ct = default)
    {
        var window = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺操作域不存在。");
        var project = await RequireMutableProjectAsync(window.ProjectId, ct).ConfigureAwait(false);
        var persistedExperiment = await store.GetExperimentAsync(experiment.ExperimentId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("独立验证实验不存在。");
        if (window.Status != OperatingRegionStatuses.Candidate ||
            persistedExperiment.ValidationOperatingRegionId != operatingRegionId ||
            persistedExperiment.ProjectId != window.ProjectId ||
            persistedExperiment.Status != ResearchExperimentStatuses.Completed ||
            result.ExperimentId != persistedExperiment.ExperimentId ||
            result.ProjectId != window.ProjectId)
            throw new ProcessResearchRuleException("验证结果与候选工艺操作域不匹配。");
        if (!result.CalculatedFromSource || !result.SafetyPassed ||
            result.RunCount < 3 || result.ReplicateCount < 3 ||
            result.DistinctBlockCount < 2 || result.RunObservations.Count < 3)
            throw new ProcessResearchRuleException("独立验证至少需要三个源数据重复运行、两个区组且全部通过安全约束。");
        if (window.Variables.Any(static value => value.LowerBound != value.UpperBound))
            throw new ProcessResearchRuleException(
                "单点重复结果不能验证连续工艺操作域；请先完成覆盖边界和交互作用的扩展实验。");
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
                "候选工艺操作域的独立跨区组重复验证结果。",
                result.AnalysisHash,
                now))
            .GroupBy(static value => (value.Kind, value.ReferenceId))
            .Select(static group => group.First())
            .ToArray();
        var validationConfidence = WilsonLowerBound(result.RunCount, result.RunCount);
        var saved = await store.SaveOperatingRegionAsync(
            window with
            {
                SupportingExperimentIds = window.SupportingExperimentIds
                    .Append(persistedExperiment.ExperimentId).Distinct().ToArray(),
                SupportingResultIds = window.SupportingResultIds
                    .Append(result.ResultId).Distinct().ToArray(),
                Evidence = evidence,
                Confidence = Math.Min(window.Confidence, validationConfidence),
                ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
                ValidationNotes = "已完成独立跨区组重复实验，等待与候选操作域创建人分离的工程师复核。",
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(window.ProjectId, "operating-region", operatingRegionId.ToString(),
            "validation-result-attached", userId, window.Status, saved.Status, ct)
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchOperatingRegion> ValidateOperatingRegionAsync(
        Guid operatingRegionId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺操作域不存在。");
        var project = await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        if (value.Status != OperatingRegionStatuses.Candidate)
            throw new ProcessResearchRuleException("只有候选工艺操作域可以进入验证状态。");
        if (project.Status != ResearchProjectStatuses.Validating)
            throw new ProcessResearchRuleException("项目进入验证阶段后才能批准工艺操作域。");
        var actor = NormalizeUser(userId);
        if (string.Equals(value.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("工艺操作域创建人和验证人必须分离。");
        if (value.Evidence.Count == 0 || value.Confidence <= 0 ||
            value.SupportingResultIds.Count == 0)
            throw new ProcessResearchRuleException("工艺操作域验证需要可追溯实验结果和统计置信度。");
        if (value.Variables.Any(static variable => variable.LowerBound != variable.UpperBound))
            throw new ProcessResearchRuleException(
                "连续工艺操作域不能由单点重复验证直接批准；请先完成覆盖边界和交互作用的扩展实验。");
        var experiments = await store.ListExperimentsAsync(value.ProjectId, ct)
            .ConfigureAwait(false);
        var validationExperimentIds = experiments
            .Where(experiment =>
                experiment.ValidationOperatingRegionId == operatingRegionId &&
                experiment.Status == ResearchExperimentStatuses.Completed)
            .Select(static experiment => experiment.ExperimentId)
            .ToHashSet();
        var validationResults = new List<ResearchExperimentResult>();
        foreach (var resultId in value.SupportingResultIds)
        {
            var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false);
            if (result is null || result.ProjectId != value.ProjectId ||
                !result.CalculatedFromSource || !result.SafetyPassed)
                throw new ProcessResearchRuleException("工艺操作域的支持结果已失效，不能通过验证。");
            if (validationExperimentIds.Contains(result.ExperimentId))
                validationResults.Add(result);
        }
        if (validationResults.Count == 0 ||
            validationResults.Any(result =>
                result.RunCount < 3 ||
                result.ReplicateCount < 3 ||
                result.DistinctBlockCount < 2))
            throw new ProcessResearchRuleException("请先完成独立验证实验；同一批候选生成数据不能替代验证证据。");
        var saved = await store.SaveOperatingRegionAsync(
            value with
            {
                Status = OperatingRegionStatuses.Validated,
                ValidationLevel = OperatingRegionValidationLevels.Laboratory,
                ValidationNotes = $"由 {actor} 独立复核跨区组重复实验、适用范围与安全约束。",
                ValidatedBy = actor,
                ValidatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "operating-region", operatingRegionId.ToString(), "validated",
            userId, value.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchOperatingRegion> ReleaseOperatingRegionAsync(
        Guid operatingRegionId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺操作域不存在。");
        var project = await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        if (value.Status != OperatingRegionStatuses.Validated ||
            value.ValidationLevel != OperatingRegionValidationLevels.Laboratory)
            throw new ProcessResearchRuleException("只有通过跨区组重复实验验证的工艺操作域才能申请生产发布。");
        var actor = NormalizeUser(userId);
        if (string.Equals(value.ValidatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("实验室验证人与生产发布人必须分离。");
        var controlledExperiments = (await store.ListExperimentsAsync(value.ProjectId, ct)
                .ConfigureAwait(false))
            .Where(experiment =>
                experiment.ExecutionCategory == ResearchExperimentExecutionCategories.ControlledOnline &&
                experiment.Optimization?.Mode == ResearchOptimizationModes.Controlled &&
                experiment.Status == ResearchExperimentStatuses.Completed &&
                experiment.ControlledDecision?.Decision is
                    ResearchControlledDecisionStatuses.Accepted or
                    ResearchControlledDecisionStatuses.Modified)
            .ToArray();
        var productionResults = new List<ResearchExperimentResult>();
        foreach (var experiment in controlledExperiments)
        {
            foreach (var resultId in experiment.ResultIds)
            {
                var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false);
                if (result is not null && result.ProjectId == value.ProjectId &&
                    result.ExperimentId == experiment.ExperimentId &&
                    result.CalculatedFromSource && result.SafetyPassed)
                {
                    productionResults.Add(result);
                }
            }
        }
        var productionObservations = productionResults
            .SelectMany(static result => result.RunObservations)
            .ToArray();
        if (productionObservations.Length < 3 || productionObservations.Any(observation =>
                !observation.ValidForOptimization ||
                !IsInsideWindow(value, observation) ||
                !MeetsMeasuredSpecification(project, observation)))
        {
            throw new ProcessResearchRuleException(
                "生产发布前必须完成至少三个来自受控在线运行的源数据结果，并确认全部位于候选操作域内且满足目标与安全约束。");
        }
        if (controlledExperiments.Any(experiment =>
                string.Equals(experiment.ControlledDecision?.DecidedBy, actor, StringComparison.Ordinal)))
            throw new ProcessResearchRuleException("受控在线决策人与生产发布人必须分离。");
        var now = DateTimeOffset.UtcNow;
        var evidence = value.Evidence
            .Concat(productionResults.Select(result => CreateEvidence(
                value.ProjectId,
                EvidenceKinds.ExperimentResult,
                result.ResultId.ToString(),
                "工艺操作域的受控在线生产验证结果。",
                result.AnalysisHash,
                now)))
            .GroupBy(static item => (item.Kind, item.ReferenceId))
            .Select(static group => group.First())
            .ToArray();
        var saved = await store.SaveOperatingRegionAsync(
            value with
            {
                ValidationLevel = OperatingRegionValidationLevels.Production,
                SupportingExperimentIds = value.SupportingExperimentIds
                    .Concat(controlledExperiments.Select(static experiment => experiment.ExperimentId))
                    .Distinct().ToArray(),
                SupportingResultIds = value.SupportingResultIds
                    .Concat(productionResults.Select(static result => result.ResultId))
                    .Distinct().ToArray(),
                Evidence = evidence,
                ValidationNotes = $"{value.ValidationNotes} 受控在线运行验证通过，由 {actor} 独立审核并发布生产。",
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
        await AuditAsync(value.ProjectId, "operating-region", operatingRegionId.ToString(),
            "production-released", userId, value.ValidationLevel, saved.ValidationLevel, ct)
            .ConfigureAwait(false);
        return saved;
    }

    private static bool IsInsideWindow(
        ResearchOperatingRegion window,
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
}
