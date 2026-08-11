using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     Evaluates transfer against a target-project cold-start result. This service never applies
///     a source window automatically: it only freezes evidence and makes negative transfer visible.
/// </summary>
public sealed class ResearchTransferAssessmentService(IProcessResearchStore store)
{
    private const double MaterialGainThreshold = 0.05;

    public async Task<ResearchTransferAssessment> AssessAsync(
        Guid targetProjectId,
        ResearchTransferAssessmentRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.SourceOperatingRegionId == Guid.Empty || request.TransferResultId == Guid.Empty ||
            request.ColdStartResultId == Guid.Empty)
            throw new ProcessResearchRuleException("迁移评估必须指定源工艺操作域、迁移结果和从零对照结果。");
        if (request.TransferResultId == request.ColdStartResultId)
            throw new ProcessResearchRuleException("迁移结果和从零对照结果必须来自不同实验结果。");

        var target = await store.GetProjectAsync(targetProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("目标研发项目不存在。");
        if (target.Status is not (ResearchProjectStatuses.Active or ResearchProjectStatuses.Validating))
            throw new ProcessResearchRuleException("迁移评估只能记录在 active 或 validating 目标项目中。");
        var window = await store.GetOperatingRegionAsync(request.SourceOperatingRegionId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("源工艺操作域不存在。");
        if (window.Status != OperatingRegionStatuses.Validated ||
            window.ValidationLevel != OperatingRegionValidationLevels.Production)
            throw new ProcessResearchRuleException("只有经过生产发布的工艺操作域可以作为迁移来源。");
        var source = await store.GetProjectAsync(window.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("源研发项目不存在。");
        var transfer = await RequireTargetResultAsync(
            request.TransferResultId, targetProjectId, "迁移", ct).ConfigureAwait(false);
        var coldStart = await RequireTargetResultAsync(
            request.ColdStartResultId, targetProjectId, "从零对照", ct).ConfigureAwait(false);

        var failures = new List<string>();
        var warnings = new List<string>();
        var contextDifferences = CompareContexts(source, target);
        if (contextDifferences.Count == 0)
            warnings.Add("源项目与目标项目没有声明可辨别的上下文差异，本记录不能证明跨条件迁移。");

        var schemaCompatible = CheckSchema(source, target, window, transfer, coldStart, failures);
        var evidenceSufficient = CheckEvidence(transfer, "迁移", failures) &
                                 CheckEvidence(coldStart, "从零对照", failures);
        if (!RunsInsideWindow(window, transfer))
        {
            evidenceSufficient = false;
            failures.Add("迁移结果的有效实际设置未全部位于源工艺操作域内，无法归因于该窗口的迁移。");
        }

        var transferLoss = schemaCompatible ? NormalizedLoss(target, transfer) : null;
        var coldStartLoss = schemaCompatible ? NormalizedLoss(target, coldStart) : null;
        if (transferLoss is null || coldStartLoss is null)
        {
            schemaCompatible = false;
            failures.Add("目标结果缺少完整且单位一致的目标指标，无法比较迁移与从零对照。");
        }

        var safetyPassed = transfer.SafetyPassed && coldStart.SafetyPassed;
        if (!transfer.SafetyPassed)
            failures.Add("迁移结果触发了安全约束，判定为负迁移。");
        if (!coldStart.SafetyPassed)
            failures.Add("从零对照结果触发了安全约束，不能作为有效收益基线。");

        double? relativeGain = transferLoss is not null && coldStartLoss is not null
            ? coldStartLoss.Value - transferLoss.Value
            : null;
        var negativeTransfer = !transfer.SafetyPassed ||
                               evidenceSufficient && schemaCompatible &&
                               relativeGain < -MaterialGainThreshold;
        var outcome = negativeTransfer
            ? ResearchTransferOutcomes.NegativeTransfer
            : !schemaCompatible || !evidenceSufficient || !safetyPassed
                ? ResearchTransferOutcomes.InsufficientEvidence
                : relativeGain > MaterialGainThreshold
                    ? ResearchTransferOutcomes.Beneficial
                    : ResearchTransferOutcomes.Neutral;
        if (outcome == ResearchTransferOutcomes.Neutral)
            warnings.Add("迁移相对从零对照没有达到 5% 的预注册实质收益阈值。");

        var actor = Required(userId, "评估人", 240).ToLowerInvariant();
        var notes = Optional(request.Notes, 4000);
        var body = new
        {
            TargetProjectId = target.ProjectId,
            TargetProjectRevision = target.Revision,
            SourceProjectId = source.ProjectId,
            SourceProjectRevision = source.Revision,
            SourceOperatingRegionId = window.OperatingRegionId,
            SourceOperatingRegionAnalysisHash = window.AnalysisHash,
            TransferResultId = transfer.ResultId,
            TransferResultAnalysisHash = transfer.AnalysisHash,
            ColdStartResultId = coldStart.ResultId,
            ColdStartResultAnalysisHash = coldStart.AnalysisHash,
            Outcome = outcome,
            SchemaCompatible = schemaCompatible,
            EvidenceSufficient = evidenceSufficient,
            SafetyPassed = safetyPassed,
            NegativeTransferDetected = negativeTransfer,
            TransferNormalizedLoss = transferLoss,
            ColdStartNormalizedLoss = coldStartLoss,
            RelativeGain = relativeGain,
            ContextDifferences = contextDifferences,
            Failures = failures.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Notes = notes,
            CreatedBy = actor
        };
        var now = DateTimeOffset.UtcNow;
        var assessment = new ResearchTransferAssessment
        {
            AssessmentId = Guid.CreateVersion7(),
            ProjectId = target.ProjectId,
            TargetProjectRevision = target.Revision,
            SourceProjectId = source.ProjectId,
            SourceProjectRevision = source.Revision,
            SourceOperatingRegionId = window.OperatingRegionId,
            SourceOperatingRegionAnalysisHash = window.AnalysisHash,
            TransferResultId = transfer.ResultId,
            TransferResultAnalysisHash = transfer.AnalysisHash,
            ColdStartResultId = coldStart.ResultId,
            ColdStartResultAnalysisHash = coldStart.AnalysisHash,
            Outcome = outcome,
            SchemaCompatible = schemaCompatible,
            EvidenceSufficient = evidenceSufficient,
            SafetyPassed = safetyPassed,
            NegativeTransferDetected = negativeTransfer,
            TransferNormalizedLoss = transferLoss,
            ColdStartNormalizedLoss = coldStartLoss,
            RelativeGain = relativeGain,
            ContextDifferences = body.ContextDifferences,
            Failures = body.Failures,
            Warnings = body.Warnings,
            Notes = notes,
            RecordHash = Hash(body),
            CreatedBy = actor,
            CreatedAt = now
        };
        var saved = await store.CreateTransferAssessmentAsync(assessment, ct).ConfigureAwait(false);
        await AuditAsync(saved, "recorded", actor, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchTransferAssessment> ReviewAsync(
        Guid assessmentId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetTransferAssessmentAsync(assessmentId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("迁移评估不存在。");
        if (value.Status == ResearchTransferAssessmentStatuses.Reviewed)
            return value;
        var actor = Required(userId, "复核人", 240).ToLowerInvariant();
        if (string.Equals(value.CreatedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("迁移评估人不能复核自己的记录。");

        var target = await store.GetProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        var source = await store.GetProjectAsync(value.SourceProjectId, ct).ConfigureAwait(false);
        var window = await store.GetOperatingRegionAsync(value.SourceOperatingRegionId, ct).ConfigureAwait(false);
        var transfer = await store.GetExperimentResultAsync(value.TransferResultId, ct).ConfigureAwait(false);
        var coldStart = await store.GetExperimentResultAsync(value.ColdStartResultId, ct).ConfigureAwait(false);
        if (target?.Revision != value.TargetProjectRevision || source?.Revision != value.SourceProjectRevision ||
            window?.AnalysisHash != value.SourceOperatingRegionAnalysisHash ||
            transfer?.AnalysisHash != value.TransferResultAnalysisHash ||
            coldStart?.AnalysisHash != value.ColdStartResultAnalysisHash)
            throw new ProcessResearchRuleException("迁移评估引用的项目版本、工艺操作域或结果已经变化，请重新评估。");

        var reviewed = value with
        {
            Status = ResearchTransferAssessmentStatuses.Reviewed,
            ReviewedBy = actor,
            ReviewedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.ReviewTransferAssessmentAsync(reviewed, ct).ConfigureAwait(false);
        await AuditAsync(saved, "reviewed", actor, ct).ConfigureAwait(false);
        return saved;
    }

    private async Task<ResearchExperimentResult> RequireTargetResultAsync(
        Guid resultId,
        Guid targetProjectId,
        string label,
        CancellationToken ct)
    {
        var result = await store.GetExperimentResultAsync(resultId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException($"{label}结果不存在。");
        if (result.ProjectId != targetProjectId)
            throw new ProcessResearchRuleException($"{label}结果不属于目标项目。");
        return result;
    }

    private static bool CheckSchema(
        ResearchProject source,
        ResearchProject target,
        ResearchOperatingRegion window,
        ResearchExperimentResult transfer,
        ResearchExperimentResult coldStart,
        ICollection<string> failures)
    {
        var compatible = true;
        if (!string.Equals(source.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("源项目和目标项目的工艺名称不同；跨工艺复用必须先建立新的机理与量纲映射。");
            compatible = false;
        }
        if (!string.IsNullOrWhiteSpace(source.SiteCode) && !string.IsNullOrWhiteSpace(target.SiteCode) &&
            !string.Equals(source.SiteCode, target.SiteCode, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("源项目和目标项目不在同一现场，当前内网部署不允许跨现场迁移引用。");
            compatible = false;
        }
        var targetVariables = target.Variables.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        foreach (var variable in window.Variables)
        {
            if (!targetVariables.TryGetValue(variable.VariableCode, out var targetVariable) ||
                targetVariable.Role != ResearchVariableRoles.Control ||
                !string.Equals(targetVariable.Unit, variable.Unit, StringComparison.Ordinal))
            {
                failures.Add($"目标项目缺少单位一致的可控变量 {variable.VariableCode}。");
                compatible = false;
                continue;
            }
            if (targetVariable.LowerLimit is { } lower && variable.LowerBound < lower ||
                targetVariable.UpperLimit is { } upper && variable.UpperBound > upper)
            {
                failures.Add($"源工艺操作域 {variable.VariableCode} 超出目标项目允许边界。");
                compatible = false;
            }
        }
        foreach (var objective in target.Objectives)
        {
            var transferMetric = transfer.Metrics.SingleOrDefault(item => item.ObjectiveCode == objective.Code);
            var coldMetric = coldStart.Metrics.SingleOrDefault(item => item.ObjectiveCode == objective.Code);
            if (transferMetric is null || coldMetric is null ||
                !string.Equals(transferMetric.Unit, objective.Unit, StringComparison.Ordinal) ||
                !string.Equals(coldMetric.Unit, objective.Unit, StringComparison.Ordinal))
            {
                failures.Add($"目标指标 {objective.Code} 在两组结果中缺失或单位不一致。");
                compatible = false;
            }
        }
        return compatible;
    }

    private static bool CheckEvidence(
        ResearchExperimentResult result,
        string label,
        ICollection<string> failures)
    {
        var validCount = result.RunObservations.Count(static item => item.ValidForOptimization);
        var sufficient = result.CalculatedFromSource && result.RunCount >= 3 &&
                         result.ReplicateCount >= 3 && result.DistinctBlockCount >= 2 && validCount >= 3;
        if (!sufficient)
            failures.Add($"{label}结果至少需要三个源数据重复、两个区组和三个有效运行。");
        return sufficient;
    }

    private static bool RunsInsideWindow(
        ResearchOperatingRegion window,
        ResearchExperimentResult result)
        => result.RunObservations.Where(static item => item.ValidForOptimization).All(observation =>
            window.Variables.All(variable => observation.ActualFactors.Any(factor =>
                factor.VariableCode == variable.VariableCode &&
                string.Equals(factor.Unit, variable.Unit, StringComparison.Ordinal) &&
                factor.Value >= variable.LowerBound && factor.Value <= variable.UpperBound)));

    private static double? NormalizedLoss(ResearchProject project, ResearchExperimentResult result)
    {
        double weightedLoss = 0;
        double totalWeight = 0;
        foreach (var objective in project.Objectives)
        {
            var metric = result.Metrics.SingleOrDefault(item => item.ObjectiveCode == objective.Code);
            if (metric is null || !string.Equals(metric.Unit, objective.Unit, StringComparison.Ordinal) ||
                !double.IsFinite(metric.ObservedValue))
                return null;
            var scale = objective.LowerLimit is { } lower && objective.UpperLimit is { } upper
                ? upper - lower
                : objective.Baseline is { } baseline && Math.Abs(baseline - objective.Target) > 1e-9
                    ? Math.Abs(baseline - objective.Target)
                    : Math.Max(Math.Abs(objective.Target), 1);
            var loss = objective.Direction switch
            {
                "minimize" => Math.Max(0, metric.ObservedValue - (objective.UpperLimit ?? objective.Target)),
                "maximize" => Math.Max(0, (objective.LowerLimit ?? objective.Target) - metric.ObservedValue),
                "range" when objective.LowerLimit is { } min && metric.ObservedValue < min => min - metric.ObservedValue,
                "range" when objective.UpperLimit is { } max && metric.ObservedValue > max => metric.ObservedValue - max,
                "range" => 0,
                _ => Math.Abs(metric.ObservedValue - objective.Target)
            } / Math.Max(scale, 1e-9);
            weightedLoss += objective.Weight * loss;
            totalWeight += objective.Weight;
        }
        return totalWeight > 0 ? weightedLoss / totalWeight : null;
    }

    private static IReadOnlyList<ResearchTransferContextDifference> CompareContexts(
        ResearchProject source,
        ResearchProject target)
    {
        var sourceContext = ProjectContext(source);
        var targetContext = ProjectContext(target);
        return sourceContext.Keys.Union(targetContext.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Where(key => !string.Equals(
                sourceContext.GetValueOrDefault(key), targetContext.GetValueOrDefault(key),
                StringComparison.OrdinalIgnoreCase))
            .Select(key => new ResearchTransferContextDifference
            {
                Field = key,
                SourceValue = sourceContext.GetValueOrDefault(key),
                TargetValue = targetContext.GetValueOrDefault(key)
            }).ToArray();
    }

    private static Dictionary<string, string> ProjectContext(ResearchProject project)
    {
        var result = project.Context.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(project.ProductName)) result["product"] = project.ProductName;
        if (!string.IsNullOrWhiteSpace(project.MaterialName)) result["material"] = project.MaterialName;
        if (!string.IsNullOrWhiteSpace(project.SiteCode)) result["site"] = project.SiteCode;
        result["process"] = project.ProcessName;
        return result;
    }

    private async Task AuditAsync(
        ResearchTransferAssessment value,
        string action,
        string actor,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = value.ProjectId,
            ResourceType = "transfer-assessment",
            ResourceId = value.AssessmentId.ToString(),
            Action = action,
            ToStatus = value.Status,
            UserId = actor,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

    private static string Required(string? value, string field, int maximumLength)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且最长 {maximumLength} 个字符。");
        return result;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result)) return null;
        if (result.Length > maximumLength)
            throw new ProcessResearchRuleException($"说明最长 {maximumLength} 个字符。");
        return result;
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
