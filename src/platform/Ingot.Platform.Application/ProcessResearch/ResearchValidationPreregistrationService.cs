// 冻结独立验证计划及其范围，防止在观察结果后改写准入规则。
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.Analytics;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>创建和验证不可在结果观测后改写的独立验证预注册。</summary>
public sealed class ResearchValidationPreregistrationService(
    IProcessResearchStore store,
    IDataReliabilityBaselineService? reliability = null)
{
    public async Task<ResearchValidationPreregistration> FreezeAsync(
        Guid projectId,
        ResearchValidationPreregistrationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status != ResearchProjectStatuses.Draft)
            throw new ProcessResearchRuleException("阶段 0 预注册必须在项目进入研发前冻结；项目变化后请在草稿阶段创建新版本。");

        var plan = Normalize(request);
        var existing = await store.ListValidationPreregistrationsAsync(projectId, ct)
            .ConfigureAwait(false);
        var version = existing.Count == 0 ? 1 : existing.Max(static value => value.Version) + 1;
        var projectSnapshotHash = ProjectDefinitionHash(project);
        var reliabilityBaseline = reliability is null
            ? EmptyReliabilityBaseline(plan)
            : await reliability.CalculateAsync(new DataReliabilityBaselineQuery
            {
                SiteId = string.IsNullOrWhiteSpace(project.SiteCode)
                    ? throw new ProcessResearchRuleException("研发项目必须绑定站点后才能冻结数据可靠性基线。")
                    : project.SiteCode.Trim(),
                From = plan.DataFrom,
                To = plan.DataTo,
                EdgeId = plan.EdgeId,
                EquipmentId = plan.EquipmentId,
                MaximumRuns = plan.MaximumRuns
            }, ct).ConfigureAwait(false);
        var contentHash = Hash(new
        {
            projectId,
            project.Revision,
            version,
            projectSnapshotHash,
            Plan = plan,
            ReliabilityBaseline = reliabilityBaseline
        });
        var duplicate = existing.FirstOrDefault(value =>
            value.ProjectRevision == project.Revision &&
            string.Equals(value.ContentHash, contentHash, StringComparison.Ordinal));
        if (duplicate is not null)
            return duplicate;

        var now = DateTimeOffset.UtcNow;
        var value = new ResearchValidationPreregistration
        {
            PreregistrationId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Version = version,
            ProjectRevision = project.Revision,
            Plan = plan,
            ReliabilityBaseline = reliabilityBaseline,
            ProjectSnapshotHash = projectSnapshotHash,
            ContentHash = contentHash,
            FrozenBy = Required(userId, "操作人", 240),
            FrozenAt = now
        };
        var saved = await store.CreateValidationPreregistrationAsync(value, ct)
            .ConfigureAwait(false);
        await AuditAsync(saved, "frozen", saved.FrozenBy, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchValidationPreregistration> ReviewAsync(
        Guid preregistrationId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetValidationPreregistrationAsync(preregistrationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("阶段 0 预注册不存在。");
        if (value.Status == ResearchValidationPreregistrationStatuses.Reviewed)
            return value;
        var actor = Required(userId, "复核人", 240);
        if (string.Equals(value.FrozenBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("阶段 0 预注册的生成人和复核人必须分离。");
        var reviewed = value with
        {
            Status = ResearchValidationPreregistrationStatuses.Reviewed,
            ReviewedBy = actor,
            ReviewedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.ReviewValidationPreregistrationAsync(reviewed, ct)
            .ConfigureAwait(false);
        await AuditAsync(saved, "reviewed", actor, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchStageZeroAdmission> AssessAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var values = await store.ListValidationPreregistrationsAsync(projectId, ct)
            .ConfigureAwait(false);
        var current = values.OrderByDescending(static value => value.Version).FirstOrDefault();
        var reviewed = current?.Status == ResearchValidationPreregistrationStatuses.Reviewed
            ? current
            : null;
        var failures = new List<string>();
        if (current is null)
            failures.Add("尚无经独立复核的阶段 0 预注册。");
        else if (reviewed is null)
            failures.Add($"当前阶段 0 预注册 v{current.Version} 尚未独立复核。");
        else
        {
            if (project.Status == ResearchProjectStatuses.Draft &&
                reviewed.ProjectRevision != project.Revision)
                failures.Add("项目定义已在预注册后变化，必须基于当前版本重新冻结并复核。");
            if (!string.Equals(
                    reviewed.ProjectSnapshotHash,
                    ProjectDefinitionHash(project),
                    StringComparison.Ordinal))
                failures.Add("项目快照与预注册哈希不一致。");
        }
        var warnings = new List<string>();
        if (reviewed?.ReliabilityBaseline.AnalyzedRunCount == 0)
            warnings.Add("冻结的数据范围内没有可分析运行；预注册有效，但数据侧尚不能支持验证结论。");
        if (reviewed?.ReliabilityBaseline.Truncated == true)
            warnings.Add("数据可靠性基线达到最大运行数，当前快照为截断结果。");
        return new ResearchStageZeroAdmission
        {
            Eligible = failures.Count == 0,
            PreregistrationId = reviewed?.PreregistrationId,
            PreregistrationVersion = reviewed?.Version,
            ContentHash = reviewed?.ContentHash,
            Failures = failures,
            Warnings = warnings
        };
    }

    public async Task RequireAsync(Guid projectId, CancellationToken ct = default)
    {
        var admission = await AssessAsync(projectId, ct).ConfigureAwait(false);
        if (!admission.Eligible)
            throw new ProcessResearchRuleException(
                $"项目未通过阶段 0 预注册门禁：{string.Join('；', admission.Failures)}");
    }

    private async Task AuditAsync(
        ResearchValidationPreregistration value,
        string action,
        string userId,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = value.ProjectId,
            ResourceType = "validation-preregistration",
            ResourceId = value.PreregistrationId.ToString(),
            Action = action,
            ToStatus = value.Status,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

    private static ResearchValidationPreregistrationRequest Normalize(
        ResearchValidationPreregistrationRequest value)
    {
        if (value.DataFrom == default || value.DataTo == default || value.DataTo <= value.DataFrom)
            throw new ProcessResearchRuleException("预注册的数据时间范围无效。");
        if (value.MaximumRuns is < 1 or > 5000)
            throw new ProcessResearchRuleException("数据可靠性基线的最大运行数必须在 1 到 5000 之间。");
        var workflows = value.EngineerWorkflowBaselines.Select(workflow =>
        {
            if (workflow.StartedAt == default || workflow.CompletedAt <= workflow.StartedAt)
                throw new ProcessResearchRuleException("工程师流程基线的开始或结束时间无效。");
            var steps = workflow.Steps.OrderBy(static step => step.Sequence).Select((step, index) =>
            {
                if (step.Sequence != index + 1 || !double.IsFinite(step.Minutes) || step.Minutes < 0)
                    throw new ProcessResearchRuleException("工程师流程步骤必须从 1 连续编号，且耗时不能为负数。");
                return step with { Name = Required(step.Name, "流程步骤", 500) };
            }).ToArray();
            if (steps.Length == 0)
                throw new ProcessResearchRuleException("至少记录一个工程师当前流程步骤。");
            return workflow with
            {
                Name = Required(workflow.Name, "流程基线名称", 500),
                Steps = steps,
                Notes = Optional(workflow.Notes, 2000)
            };
        }).ToArray();
        if (workflows.Length == 0)
            throw new ProcessResearchRuleException("至少记录一条工程师当前流程基线。");
        return value with
        {
            DataScope = Required(value.DataScope, "数据范围", 4000),
            EdgeId = Optional(value.EdgeId, 500),
            EquipmentId = Optional(value.EquipmentId, 500),
            InclusionMethod = Required(value.InclusionMethod, "纳入方式", 2000),
            InclusionRules = RequiredList(value.InclusionRules, "纳入规则"),
            ExclusionRules = RequiredList(value.ExclusionRules, "排除规则"),
            MatchingRules = RequiredList(value.MatchingRules, "匹配规则"),
            BaselineMethods = RequiredList(value.BaselineMethods, "比较基线"),
            PrimaryMetrics = RequiredList(value.PrimaryMetrics, "主要指标"),
            GuardrailMetrics = RequiredList(value.GuardrailMetrics, "守门指标"),
            StopConditions = RequiredList(value.StopConditions, "停止条件"),
            FalsificationConditions = RequiredList(value.FalsificationConditions, "否证条件"),
            EngineerWorkflowBaselines = workflows
        };
    }

    private static IReadOnlyList<string> RequiredList(IReadOnlyList<string> values, string name)
    {
        var normalized = values.Select(value => Required(value, name, 1000))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0)
            throw new ProcessResearchRuleException($"{name}至少需要一项。");
        if (normalized.Length > 100)
            throw new ProcessResearchRuleException($"{name}不能超过 100 项。");
        return normalized;
    }

    private static string Required(string? value, string name, int maximumLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumLength)
            throw new ProcessResearchRuleException($"{name}无效。");
        return result;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) return null;
        if (result.Length > maximumLength)
            throw new ProcessResearchRuleException("可选说明过长。");
        return result;
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static DataReliabilityBaseline EmptyReliabilityBaseline(
        ResearchValidationPreregistrationRequest plan)
        => new()
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            From = plan.DataFrom,
            To = plan.DataTo,
            EdgeId = plan.EdgeId,
            EquipmentId = plan.EquipmentId
        };

    private static string ProjectDefinitionHash(ResearchProject project)
        => Hash(new
        {
            project.ProjectId,
            project.Code,
            project.Name,
            project.ProcessName,
            project.ProductName,
            project.MaterialName,
            project.Description,
            project.Objectives,
            project.Variables,
            project.Constraints,
            project.OutcomeConstraints,
            project.SafetyTemplates,
            project.OptimizationFeatures,
            Context = project.Context
                .Where(static pair => pair.Key != ResearchContextAdmissionEvaluator.PolicyHashContextKey)
                .OrderBy(static pair => pair.Key),
            project.SiteCode
        });
}
