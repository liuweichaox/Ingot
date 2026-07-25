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

        return await store.SaveProjectAsync(value, ct).ConfigureAwait(false);
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
                OwnerUserId = string.IsNullOrWhiteSpace(request.OwnerUserId)
                    ? existing.OwnerUserId
                    : NormalizeUser(request.OwnerUserId),
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = existing.Revision + 1
            });
        return await store.SaveProjectAsync(value, ct).ConfigureAwait(false);
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
            if (windows.All(static value => value.Status != ProcessWindowStatuses.Validated))
                throw new ProcessResearchRuleException("研发项目完成前必须形成经过验证的工艺窗口。");
        }

        return await store.SaveProjectAsync(
            project with
            {
                Status = targetStatus,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = project.Revision + 1
            },
            ct).ConfigureAwait(false);
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
        if (!ResearchHypothesisStatuses.IsValid(request.Status))
            throw new ProcessResearchRuleException("研发假设状态无效。");
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
            SupportingEvidence = NormalizeEvidence(request.SupportingEvidence),
            OpposingEvidence = NormalizeEvidence(request.OpposingEvidence),
            CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        return await store.SaveHypothesisAsync(value, ct).ConfigureAwait(false);
    }

    public async Task<ResearchExperiment> CreateExperimentAsync(
        Guid projectId,
        ResearchExperiment request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        if (request.HypothesisId is { } hypothesisId)
        {
            var hypothesis = await store.GetHypothesisAsync(hypothesisId, ct).ConfigureAwait(false);
            if (hypothesis is null || hypothesis.ProjectId != projectId)
                throw new ProcessResearchRuleException("实验引用的研发假设不存在于当前项目。");
        }

        var knownVariables = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (request.Factors.Count == 0)
            throw new ProcessResearchRuleException("实验必须包含至少一个可控变量设置。");
        var factors = request.Factors.Select(value =>
        {
            var code = NormalizeCode(value.VariableCode, "实验变量");
            if (!knownVariables.TryGetValue(code, out var variable))
                throw new ProcessResearchRuleException($"实验变量 {code} 不是项目中的可控变量。");
            if (!double.IsFinite(value.Value) ||
                variable.LowerLimit is { } lower && value.Value < lower ||
                variable.UpperLimit is { } upper && value.Value > upper)
                throw new ProcessResearchRuleException($"实验变量 {code} 超出允许范围。");
            return value with { VariableCode = code, Unit = RequiredText(value.Unit, "实验变量单位", 40) };
        }).ToArray();
        if (factors.Select(static value => value.VariableCode).Distinct(StringComparer.Ordinal).Count() !=
            factors.Length)
            throw new ProcessResearchRuleException("同一实验变量只能设置一次。");

        var objectiveCodes = NormalizeCodes(request.ObjectiveCodes, "实验目标");
        var knownObjectives = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (objectiveCodes.Count == 0 || objectiveCodes.Any(code => !knownObjectives.Contains(code)))
            throw new ProcessResearchRuleException("实验必须引用项目中已经定义的目标。");

        var now = DateTimeOffset.UtcNow;
        var value = request with
        {
            ExperimentId = request.ExperimentId == Guid.Empty
                ? Guid.CreateVersion7()
                : request.ExperimentId,
            ProjectId = projectId,
            Name = RequiredText(request.Name, "实验名称", 240),
            DesignMethod = RequiredText(request.DesignMethod, "实验设计方法", 120).ToLowerInvariant(),
            Status = ResearchExperimentStatuses.Planned,
            Factors = factors,
            ObjectiveCodes = objectiveCodes,
            ReplicateKeys = request.ReplicateKeys.Select(static value => value.Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
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
        return await store.SaveExperimentAsync(value, ct).ConfigureAwait(false);
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

        return await store.SaveExperimentAsync(
            experiment with
            {
                Status = targetStatus,
                ApprovedBy = targetStatus == ResearchExperimentStatuses.Approved
                    ? actor
                    : experiment.ApprovedBy,
                ApprovedAt = targetStatus == ResearchExperimentStatuses.Approved
                    ? DateTimeOffset.UtcNow
                    : experiment.ApprovedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
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
                value.LowerBound >= value.UpperBound ||
                variable.LowerLimit is { } lower && value.LowerBound < lower ||
                variable.UpperLimit is { } upper && value.UpperBound > upper)
                throw new ProcessResearchRuleException($"工艺窗口变量 {code} 的范围无效。");
            return value with
            {
                VariableCode = code,
                Unit = RequiredText(value.Unit, "工艺窗口变量单位", 40)
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
        if (request.Confidence is < 0 or > 1 || !double.IsFinite(request.Confidence))
            throw new ProcessResearchRuleException("工艺窗口置信度必须位于 0 到 1 之间。");

        var now = DateTimeOffset.UtcNow;
        var existing = request.WindowId == Guid.Empty
            ? null
            : await store.GetProcessWindowAsync(request.WindowId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("工艺窗口不属于当前项目。");
        if (existing?.Status == ProcessWindowStatuses.Validated)
            throw new ProcessResearchRuleException("经过验证的工艺窗口保持不可变。");

        return await store.SaveProcessWindowAsync(
            request with
            {
                WindowId = existing?.WindowId ??
                           (request.WindowId == Guid.Empty ? Guid.CreateVersion7() : request.WindowId),
                ProjectId = projectId,
                Name = RequiredText(request.Name, "工艺窗口名称", 240),
                Status = ProcessWindowStatuses.Candidate,
                Variables = variables,
                ObjectiveCodes = NormalizeCodes(request.ObjectiveCodes, "工艺窗口目标"),
                SupportingExperimentIds = request.SupportingExperimentIds.Distinct().ToArray(),
                Evidence = NormalizeEvidence(request.Evidence),
                Applicability = RequiredText(request.Applicability, "工艺窗口适用范围", 8000),
                ValidatedBy = null,
                ValidatedAt = null,
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
    }

    public async Task<ResearchProcessWindow> ValidateProcessWindowAsync(
        Guid windowId,
        string userId,
        CancellationToken ct = default)
    {
        var value = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("工艺窗口不存在。");
        await RequireMutableProjectAsync(value.ProjectId, ct).ConfigureAwait(false);
        if (value.Status != ProcessWindowStatuses.Candidate)
            throw new ProcessResearchRuleException("只有候选工艺窗口可以进入验证状态。");
        if (value.Evidence.Count == 0 || value.Confidence <= 0)
            throw new ProcessResearchRuleException("工艺窗口验证需要证据和置信度。");

        return await store.SaveProcessWindowAsync(
            value with
            {
                Status = ProcessWindowStatuses.Validated,
                ValidatedBy = NormalizeUser(userId),
                ValidatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
    }

    public async Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        Guid projectId,
        ResearchKnowledgeClaim request,
        string userId,
        CancellationToken ct = default)
    {
        await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        if (request.ProcessWindowId is { } windowId)
        {
            var window = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false);
            if (window is null || window.ProjectId != projectId ||
                window.Status != ProcessWindowStatuses.Validated)
                throw new ProcessResearchRuleException("知识声明只能引用当前项目中经过验证的工艺窗口。");
        }
        var now = DateTimeOffset.UtcNow;
        var existing = request.ClaimId == Guid.Empty
            ? null
            : await store.GetKnowledgeClaimAsync(request.ClaimId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("知识声明不属于当前项目。");
        if (existing?.Status is ResearchKnowledgeStatuses.Published or ResearchKnowledgeStatuses.Retired)
            throw new ProcessResearchRuleException("已发布或已停用的知识声明保持不可变。");

        return await store.SaveKnowledgeClaimAsync(
            request with
            {
                ClaimId = existing?.ClaimId ??
                          (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId),
                ProjectId = projectId,
                Statement = RequiredText(request.Statement, "知识声明", 8000),
                Applicability = RequiredText(request.Applicability, "知识适用范围", 8000),
                Status = ResearchKnowledgeStatuses.Draft,
                Evidence = NormalizeEvidence(request.Evidence),
                CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
                ReviewedBy = null,
                ReviewedAt = null,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            ct).ConfigureAwait(false);
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

        return await store.SaveKnowledgeClaimAsync(
            value with
            {
                Status = ResearchKnowledgeStatuses.Reviewed,
                ReviewedBy = actor,
                ReviewedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            ct).ConfigureAwait(false);
    }

    public async Task<ResearchProjectWorkspace> GetWorkspaceAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        return new ResearchProjectWorkspace
        {
            Project = project,
            Hypotheses = await store.ListHypothesesAsync(projectId, ct).ConfigureAwait(false),
            Experiments = await store.ListExperimentsAsync(projectId, ct).ConfigureAwait(false),
            ProcessWindows = await store.ListProcessWindowsAsync(projectId, ct).ConfigureAwait(false),
            KnowledgeClaims = await store.ListKnowledgeClaimsAsync(projectId, ct).ConfigureAwait(false)
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
        var constraints = value.Constraints.Select(item =>
        {
            var variableCode = NormalizeCode(item.VariableCode, "约束变量");
            if (!knownVariables.Contains(variableCode))
                throw new ProcessResearchRuleException($"约束引用了未定义变量 {variableCode}。");
            if (!double.IsFinite(item.Limit))
                throw new ProcessResearchRuleException("约束限值必须是有限数值。");
            return item with
            {
                Code = NormalizeCode(item.Code, "约束代码"),
                Description = RequiredText(item.Description, "约束说明", 1000),
                VariableCode = variableCode,
                Operator = item.Operator.Trim(),
                Unit = RequiredText(item.Unit, "约束单位", 40)
            };
        }).ToArray();
        if (constraints.Select(static item => item.Code).Distinct(StringComparer.Ordinal).Count() !=
            constraints.Length)
            throw new ProcessResearchRuleException("约束代码不能重复。");

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
            Context = value.Context
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) &&
                                      !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    static pair => pair.Key.Trim().ToLowerInvariant(),
                    static pair => pair.Value.Trim(),
                    StringComparer.Ordinal)
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
            Direction = direction
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
        IReadOnlyList<EvidenceReference> source)
        => source.Select(value => value with
        {
            Kind = RequiredText(value.Kind, "证据类型", 80).ToLowerInvariant(),
            ReferenceId = RequiredText(value.ReferenceId, "证据标识", 500),
            Summary = RequiredText(value.Summary, "证据摘要", 2000),
            ContentHash = OptionalText(value.ContentHash, 128)
        }).ToArray();

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
}
