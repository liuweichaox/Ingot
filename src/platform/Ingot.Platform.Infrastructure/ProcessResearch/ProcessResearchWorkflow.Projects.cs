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
                Context = WithoutClientPolicyHash(draft.Context),
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

        var updatedContext = WithoutClientPolicyHash(request.Context);
        if (existing.Status != ResearchProjectStatuses.Draft &&
            !string.Equals(
                ContextValue(existing.Context, ResearchContextAdmissionEvaluator.ScenarioPackageContextKey),
                ContextValue(updatedContext, ResearchContextAdmissionEvaluator.ScenarioPackageContextKey),
                StringComparison.OrdinalIgnoreCase))
            throw new ProcessResearchRuleException("研发项目进入执行阶段后不能更换工艺配置版本。");
        if (existing.Context.TryGetValue(
                ResearchContextAdmissionEvaluator.PolicyHashContextKey,
                out var frozenPolicyHash))
            updatedContext[ResearchContextAdmissionEvaluator.PolicyHashContextKey] = frozenPolicyHash;

        var value = NormalizeProject(
            request with
            {
                ProjectId = existing.ProjectId,
                Code = existing.Code,
                Status = existing.Status,
                OwnerUserId = existing.OwnerUserId,
                Context = updatedContext,
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
        if (project.Status == ResearchProjectStatuses.Draft &&
            targetStatus == ResearchProjectStatuses.Active)
            await new ResearchValidationPreregistrationService(store)
                .RequireAsync(projectId, ct).ConfigureAwait(false);
        var projectContext = targetStatus == ResearchProjectStatuses.Active
            ? await FreezeContextPolicyAsync(project, ct).ConfigureAwait(false)
            : project.Context;
        if (targetStatus == ResearchProjectStatuses.Completed)
        {
            var windows = await store.ListOperatingRegionsAsync(projectId, ct).ConfigureAwait(false);
            if (windows.All(static value =>
                    value.Status != OperatingRegionStatuses.Validated ||
                    value.ValidationLevel is not (
                        OperatingRegionValidationLevels.Laboratory or
                        OperatingRegionValidationLevels.Production)))
                throw new ProcessResearchRuleException(
                    "研发项目完成前必须形成经过跨区组重复实验验证的工艺操作域。");
        }

        var saved = await store.SaveProjectAsync(
            project with
            {
                Status = targetStatus,
                Context = projectContext,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = project.Revision + 1
            },
            ct).ConfigureAwait(false);
        await AuditAsync(projectId, "project", projectId.ToString(), "status-changed",
            userId, project.Status, saved.Status, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchProjectWorkspace> GetWorkspaceAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        const int workspaceHistoryLimit = 100;
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        var hypothesesTask = store.ListHypothesesAsync(projectId, ct);
        var experimentsTask = store.ListExperimentsPageAsync(
            projectId, null, workspaceHistoryLimit, ct);
        var resultsTask = store.ListExperimentResultsPageAsync(
            projectId, null, workspaceHistoryLimit, ct);
        var shadowTask = store.ListShadowRecommendationsPageAsync(
            projectId, null, workspaceHistoryLimit, ct);
        var replayTask = store.ListHistoricalReplayReportsPageAsync(
            projectId, null, workspaceHistoryLimit, ct);
        var rollbackTask = store.ListRollbackDrillsAsync(projectId, ct);
        var windowsTask = store.ListOperatingRegionsAsync(projectId, ct);
        var claimsTask = store.ListKnowledgeClaimsAsync(projectId, ct);
        var mechanismUsagesTask = mechanismKnowledgeStore?.ListUsagesAsync(projectId, ct)
            ?? Task.FromResult<IReadOnlyList<Ingot.Contracts.ResearchAssets.MechanismClaimUsage>>([]);
        var transfersTask = store.ListTransferAssessmentsAsync(projectId, ct);
        var preregistrationsTask = store.ListValidationPreregistrationsAsync(projectId, ct);
        var stageZeroAdmissionTask = new ResearchValidationPreregistrationService(store)
            .AssessAsync(projectId, ct);
        var auditTask = store.ListAuditEntriesPageAsync(
            projectId, null, workspaceHistoryLimit, ct);
        await Task.WhenAll(
            hypothesesTask,
            experimentsTask,
            resultsTask,
            shadowTask,
            replayTask,
            rollbackTask,
            windowsTask,
            claimsTask,
            mechanismUsagesTask,
            transfersTask,
            preregistrationsTask,
            stageZeroAdmissionTask,
            auditTask).ConfigureAwait(false);
        return new ResearchProjectWorkspace
        {
            Project = project,
            Hypotheses = await hypothesesTask.ConfigureAwait(false),
            Experiments = (await experimentsTask.ConfigureAwait(false)).Items,
            ExperimentResults = (await resultsTask.ConfigureAwait(false)).Items,
            ShadowRecommendations = (await shadowTask.ConfigureAwait(false)).Items,
            HistoricalReplayReports = (await replayTask.ConfigureAwait(false)).Items,
            RollbackDrills = await rollbackTask.ConfigureAwait(false),
            OperatingRegions = await windowsTask.ConfigureAwait(false),
            KnowledgeClaims = await claimsTask.ConfigureAwait(false),
            MechanismKnowledgeUsages = await mechanismUsagesTask.ConfigureAwait(false),
            TransferAssessments = await transfersTask.ConfigureAwait(false),
            ValidationPreregistrations = await preregistrationsTask.ConfigureAwait(false),
            StageZeroAdmission = await stageZeroAdmissionTask.ConfigureAwait(false),
            Audit = (await auditTask.ConfigureAwait(false)).Items,
            NextCursors = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["experiments"] = (await experimentsTask.ConfigureAwait(false)).NextCursor ?? "",
                ["experiment-results"] = (await resultsTask.ConfigureAwait(false)).NextCursor ?? "",
                ["shadow-recommendations"] = (await shadowTask.ConfigureAwait(false)).NextCursor ?? "",
                ["historical-replays"] = (await replayTask.ConfigureAwait(false)).NextCursor ?? "",
                ["audit"] = (await auditTask.ConfigureAwait(false)).NextCursor ?? ""
            }.Where(static pair => pair.Value.Length > 0)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
        };
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
        var safetyTemplates = value.SafetyTemplates.Select(item =>
        {
            var category = item.ExecutionCategory.Trim().ToLowerInvariant();
            if (!ResearchExperimentExecutionCategories.IsValid(category))
                throw new ProcessResearchRuleException("实验安全模板的执行类别无效。");
            return item with
            {
                ExecutionCategory = category,
                Name = OptionalText(item.Name, 120),
                StopRule = RequiredText(item.StopRule, "模板停止规则", 4000),
                RollbackPlan = RequiredText(item.RollbackPlan, "模板回退方案", 4000)
            };
        }).GroupBy(static item => item.ExecutionCategory, StringComparer.Ordinal)
            .Select(static group => group.Last()).ToArray();

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
            SafetyTemplates = safetyTemplates,
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

    private async Task<IReadOnlyDictionary<string, string>> FreezeContextPolicyAsync(
        ResearchProject project,
        CancellationToken ct)
    {
        if (!ResearchContextAdmissionEvaluator.TryParseScenarioPackageReference(
                project.Context,
                out var packageId,
                out var version))
            return project.Context;
        if (processConfigurations is null)
            throw new ProcessResearchRuleException("当前运行时无法验证研发项目引用的工艺配置。");
        var package = await processConfigurations.GetScenarioPackageAsync(packageId, version, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException($"研发项目引用的工艺配置不存在：{packageId} v{version}。");
        if (package.Status == ConfigurationStatuses.Draft)
            throw new ProcessResearchRuleException("研发项目进入执行阶段前必须使用已发布的工艺配置版本。");
        var policyHash = ResearchContextAdmissionEvaluator.ComputePolicyHash(package);
        if (project.Context.TryGetValue(
                ResearchContextAdmissionEvaluator.PolicyHashContextKey,
                out var existingHash) &&
            !string.Equals(existingHash, policyHash, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("研发项目冻结的上下文策略哈希与工艺配置不一致。");
        return new Dictionary<string, string>(project.Context, StringComparer.Ordinal)
        {
            [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                $"{package.PackageId}:{package.Version}",
            [ResearchContextAdmissionEvaluator.PolicyHashContextKey] = policyHash
        };
    }

    private static Dictionary<string, string> WithoutClientPolicyHash(
        IReadOnlyDictionary<string, string> context)
        => context
            .Where(static pair => !string.Equals(
                pair.Key,
                ResearchContextAdmissionEvaluator.PolicyHashContextKey,
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static string? ContextValue(
        IReadOnlyDictionary<string, string> context,
        string key)
        => context.FirstOrDefault(pair =>
            string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
}
