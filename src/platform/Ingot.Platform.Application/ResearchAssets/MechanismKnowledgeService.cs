using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>管理机理知识的版本、审阅和适用范围，不反向编排配方优化工作流。</summary>
public sealed class MechanismKnowledgeService(
    IMechanismKnowledgeStore store,
    IResearchProjectContextReader projectReader)
{
    private static readonly IReadOnlySet<string> ApplicabilityDimensions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "project-code", "process", "product", "material", "equipment", "tooling",
            "process-specification", "phase", "site"
        };
    private static readonly IReadOnlySet<string> VariableRoles =
        new HashSet<string>(["cause", "mediator", "outcome", "moderator"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Directions =
        new HashSet<string>(["increase", "decrease", "nonlinear"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ConstraintKinds =
        new HashSet<string>(["range", "safe-range", "preferred-range"], StringComparer.Ordinal);

    public async Task<MechanismClaimVersion> SaveDraftAsync(
        Guid projectId,
        MechanismClaimVersion request,
        string userId,
        CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            throw new ResearchAssetRuleException("必须指定研发项目。");
        var actor = Required(userId, "创建人", 200);
        var existing = request.ClaimId == Guid.Empty
            ? null
            : await store.GetClaimAsync(request.ClaimId, null, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ResearchAssetRuleException("机理声明不属于当前研发项目。");
        if (existing is not null && existing.Status != MechanismClaimStatuses.Draft)
            throw new ResearchAssetRuleException("已进入审核流程的声明不可覆盖，请创建新的机理声明。");

        var type = Required(request.MechanismType, "机理类型", 80).ToLowerInvariant();
        if (!MechanismClaimTypes.All.Contains(type))
            throw new ResearchAssetRuleException("机理类型无效。");
        var variables = request.Variables.Select(NormalizeVariable).DistinctBy(
            value => (value.VariableCode, value.VariableRole)).ToArray();
        if (variables.Length == 0)
            throw new ResearchAssetRuleException("机理声明至少需要一个变量。");
        var applicability = request.Applicability.Select(value => new MechanismClaimApplicability
        {
            DimensionCode = NormalizeDimension(value.DimensionCode),
            DimensionValue = Required(value.DimensionValue, "适用实体代码", 300).ToLowerInvariant()
        }).DistinctBy(value => (value.DimensionCode, value.DimensionValue)).ToArray();
        if (applicability.Length == 0)
            throw new ResearchAssetRuleException("适用范围不能为空；空范围不代表全局适用。");
        var constraints = request.Constraints.Select(NormalizeConstraint).ToArray();
        var forbiddenCombinations = request.ForbiddenCombinations
            .Select(NormalizeForbiddenCombination).ToArray();
        var evidence = request.Evidence.Select(NormalizeEvidence).DistinctBy(
            value => (value.EvidenceKind, value.ReferenceId, value.Polarity)).ToArray();
        if (evidence.Length == 0)
            throw new ResearchAssetRuleException("机理声明至少需要一个可追溯证据引用。");
        foreach (var item in evidence)
            if (!await store.EvidenceExistsAsync(projectId, item, ct).ConfigureAwait(false))
                throw new ResearchAssetRuleException("证据引用不存在、不属于当前项目或内容哈希不匹配。");
        var project = await projectReader.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("研发项目不存在。");
        ValidateProjectBindings(project, variables, constraints, forbiddenCombinations, applicability);

        var now = DateTimeOffset.UtcNow;
        var claimId = existing?.ClaimId ?? (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId);
        var version = existing is null ? 1 : existing.Version + 1;
        var value = new MechanismClaimVersion
        {
            ClaimId = claimId,
            ProjectId = projectId,
            Version = version,
            Status = MechanismClaimStatuses.Draft,
            Name = Required(request.Name, "声明名称", 240),
            MechanismType = type,
            Statement = Required(request.Statement, "机理陈述", 8000),
            ExpectedSignature = Optional(request.ExpectedSignature, 4000),
            FalsificationCondition = Required(request.FalsificationCondition, "反证条件", 8000),
            EvidenceLevel = Required(request.EvidenceLevel, "证据等级", 100).ToLowerInvariant(),
            Variables = variables,
            Applicability = applicability,
            Constraints = constraints,
            ForbiddenCombinations = forbiddenCombinations,
            Evidence = evidence,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = "pending"
        };
        value = value with { ContentHash = ComputeHash(value) };
        return await store.SaveDraftAsync(value, ct).ConfigureAwait(false);
    }

    public async Task<MechanismClaimVersion> ReviewAsync(
        Guid claimId,
        MechanismClaimReviewRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var claim = await store.GetClaimAsync(claimId, null, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理声明不存在。");
        var actor = Required(userId, "审核人", 200);
        if (claim.Status != MechanismClaimStatuses.Draft)
            throw new ResearchAssetRuleException("只有草稿声明可以审核。");
        if (string.Equals(claim.CreatedBy, actor, StringComparison.Ordinal))
            throw new ResearchAssetRuleException("机理声明创建人和审核人必须分离。");
        var decision = Required(request.Decision, "审核决定", 20).ToLowerInvariant();
        if (decision is not ("approve" or "reject"))
            throw new ResearchAssetRuleException("审核决定只能是 approve 或 reject。");
        if (decision == "approve" && (claim.Evidence.Count == 0 || claim.Applicability.Count == 0))
            throw new ResearchAssetRuleException("通过审核前必须具备证据和明确适用范围。");
        var review = new MechanismClaimReview
        {
            ReviewId = Guid.CreateVersion7(),
            ClaimId = claim.ClaimId,
            ClaimVersion = claim.Version,
            Decision = decision,
            ReviewerId = actor,
            Comment = Optional(request.Comment, 4000),
            ReviewedAt = DateTimeOffset.UtcNow
        };
        return await store.AddReviewAsync(
            review,
            decision == "approve" ? MechanismClaimStatuses.Reviewed : MechanismClaimStatuses.Rejected,
            ct).ConfigureAwait(false);
    }

    public async Task<MechanismClaimConflict> AddConflictAsync(
        Guid projectId,
        MechanismClaimConflictRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.LeftClaimId == request.RightClaimId)
            throw new ResearchAssetRuleException("冲突两侧必须是不同声明。");
        var left = await store.GetClaimAsync(request.LeftClaimId, request.LeftClaimVersion, ct).ConfigureAwait(false);
        var right = await store.GetClaimAsync(request.RightClaimId, request.RightClaimVersion, ct).ConfigureAwait(false);
        if (left is null || right is null || left.ProjectId != projectId || right.ProjectId != projectId)
            throw new ResearchAssetRuleException("冲突声明必须存在且属于当前研发项目。");
        return await store.AddConflictAsync(new MechanismClaimConflict
        {
            ConflictId = Guid.CreateVersion7(),
            ProjectId = projectId,
            LeftClaimId = left.ClaimId,
            LeftClaimVersion = left.Version,
            RightClaimId = right.ClaimId,
            RightClaimVersion = right.Version,
            ConflictKind = Required(request.ConflictKind, "冲突类型", 100).ToLowerInvariant(),
            Rationale = Required(request.Rationale, "冲突说明", 4000),
            CreatedBy = Required(userId, "创建人", 200),
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
    }

    public async Task<MechanismClaimConflict> ResolveConflictAsync(
        Guid projectId,
        Guid conflictId,
        MechanismClaimConflictResolutionRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var conflict = await store.GetConflictAsync(conflictId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理冲突不存在。");
        if (conflict.ProjectId != projectId)
            throw new ResearchAssetRuleException("机理冲突不属于当前研发项目。");
        if (conflict.Status != "open") return conflict;
        var actor = Required(userId, "解决人", 200);
        if (string.Equals(actor, conflict.CreatedBy, StringComparison.Ordinal))
            throw new ResearchAssetRuleException("冲突登记人不能独自解决该冲突。");
        return await store.ResolveConflictAsync(conflict with
        {
            Status = "resolved",
            ResolvedBy = actor,
            ResolvedAt = DateTimeOffset.UtcNow,
            Resolution = Required(request.Resolution, "解决结论", 4000)
        }, ct).ConfigureAwait(false);
    }

    public async Task<MechanismClaimVersion> TransitionAsync(
        Guid projectId,
        Guid claimId,
        MechanismClaimLifecycleRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var claim = await store.GetClaimAsync(claimId, null, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理声明不存在。");
        if (claim.ProjectId != projectId)
            throw new ResearchAssetRuleException("机理声明不属于当前研发项目。");
        var actor = Required(userId, "操作人", 200);
        var target = Required(request.TargetStatus, "目标状态", 20).ToLowerInvariant();
        var expectedTarget = claim.Status switch
        {
            MechanismClaimStatuses.Reviewed => MechanismClaimStatuses.Supported,
            MechanismClaimStatuses.Supported => MechanismClaimStatuses.Validated,
            MechanismClaimStatuses.Validated => MechanismClaimStatuses.Active,
            MechanismClaimStatuses.Active => MechanismClaimStatuses.Retired,
            _ => null
        };
        var isFalsification = target == MechanismClaimStatuses.Falsified && claim.Status is
            MechanismClaimStatuses.Reviewed or MechanismClaimStatuses.Supported or
            MechanismClaimStatuses.Validated or MechanismClaimStatuses.Active;
        if (target != expectedTarget && !isFalsification)
            throw new ResearchAssetRuleException("机理声明必须按已复核、已支持、已验证、生效、退休的顺序流转。");
        if (string.Equals(actor, claim.CreatedBy, StringComparison.Ordinal) ||
            string.Equals(actor, claim.ReviewedBy, StringComparison.Ordinal))
            throw new ResearchAssetRuleException("创建人和结构审核人不能执行证据升级或激活决定。");
        if (await store.LifecycleActorUsedAsync(claimId, actor, ct).ConfigureAwait(false))
            throw new ResearchAssetRuleException("同一人员不能连续承担机理支持、独立验证或激活决定。");

        string? evidenceKind = null;
        string? referenceId = null;
        string? contentHash = null;
        if (target is MechanismClaimStatuses.Supported or MechanismClaimStatuses.Validated || isFalsification)
        {
            evidenceKind = Required(request.EvidenceKind, "验证证据类型", 80).ToLowerInvariant();
            if (evidenceKind != "recipe-recommendation-outcome")
                throw new ResearchAssetRuleException("支持和验证升级必须引用已冻结的配方建议实际运行结果。");
            referenceId = Required(request.ReferenceId, "配方建议决定引用", 500);
            contentHash = Required(request.ContentHash, "真实运行结果哈希", 64).ToLowerInvariant();
            if (!Regex.IsMatch(contentHash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                throw new ResearchAssetRuleException("真实运行结果哈希必须是 64 位 SHA-256。");
            if (request.ValidationHypothesisId is not { } validationHypothesisId)
                throw new ResearchAssetRuleException("支持和验证升级必须指定关联的研发假设。");
            var evaluationOutcome = Required(request.EvaluationOutcome, "证据评价结论", 20).ToLowerInvariant();
            var expectedOutcome = isFalsification ? "falsifies" : "supports";
            if (evaluationOutcome != expectedOutcome)
                throw new ResearchAssetRuleException(isFalsification
                    ? "反证声明必须引用明确不满足预注册效应的真实运行结果。"
                    : "只有明确支持声明的真实运行结果评价才能升级。");
            var evaluationSummary = Required(request.EvaluationSummary, "证据评价说明", 4000);
            var evidence = new MechanismClaimEvidence
            {
                EvidenceKind = evidenceKind,
                ReferenceId = referenceId,
                ContentHash = contentHash
            };
            if (!await store.RecipeRecommendationOutcomeSupportsClaimAsync(
                    projectId, claim, validationHypothesisId, evidence, evaluationOutcome, ct).ConfigureAwait(false))
                throw new ResearchAssetRuleException(
                    "真实运行结果必须来自已完成的配方建议闭环，并通过安全与源数据校验。");
            if (await store.LifecycleEvidenceUsedAsync(claimId, referenceId, ct).ConfigureAwait(false))
                throw new ResearchAssetRuleException("同一真实运行结果不能重复用于机理知识升级。");
        }
        if (target == MechanismClaimStatuses.Active)
        {
            var hasOpenConflict = (await store.ListConflictsAsync(projectId, ct).ConfigureAwait(false))
                .Any(value => value.Status == "open" &&
                    (value.LeftClaimId == claimId || value.RightClaimId == claimId));
            if (hasOpenConflict)
                throw new ResearchAssetRuleException("存在未解决冲突的机理声明不能激活。");
        }
        return await store.TransitionAsync(new MechanismClaimLifecycleDecision
        {
            DecisionId = Guid.CreateVersion7(),
            ClaimId = claim.ClaimId,
            ClaimVersion = claim.Version,
            FromStatus = claim.Status,
            ToStatus = target,
            EvidenceKind = evidenceKind,
            ReferenceId = referenceId,
            ContentHash = contentHash,
            ValidationHypothesisId = request.ValidationHypothesisId,
            EvaluationOutcome = target is MechanismClaimStatuses.Supported or MechanismClaimStatuses.Validated or MechanismClaimStatuses.Falsified
                ? request.EvaluationOutcome.Trim().ToLowerInvariant()
                : null,
            EvaluationSummary = target is MechanismClaimStatuses.Supported or MechanismClaimStatuses.Validated or MechanismClaimStatuses.Falsified
                ? request.EvaluationSummary!.Trim()
                : null,
            Comment = Optional(request.Comment, 4000),
            DecidedBy = actor,
            DecidedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
    }

    private static MechanismClaimVariable NormalizeVariable(MechanismClaimVariable value)
    {
        var role = Required(value.VariableRole, "变量作用", 80).ToLowerInvariant();
        var direction = Optional(value.Direction, 40)?.ToLowerInvariant();
        if (!VariableRoles.Contains(role))
            throw new ResearchAssetRuleException("变量作用只能是 cause、mediator、outcome 或 moderator。");
        if (direction is not null && !Directions.Contains(direction))
            throw new ResearchAssetRuleException("变量方向只能是 increase、decrease 或 nonlinear。");
        if (value.DelayMilliseconds is < 0)
            throw new ResearchAssetRuleException("变量时滞不能为负数。");
        return new MechanismClaimVariable
        {
            VariableCode = Required(value.VariableCode, "变量代码", 200).ToLowerInvariant(),
            VariableRole = role,
            Direction = direction,
            DelayMilliseconds = value.DelayMilliseconds,
            Unit = NormalizeUnit(value.Unit)
        };
    }

    private static MechanismClaimConstraint NormalizeConstraint(MechanismClaimConstraint value)
    {
        if (value.Minimum is null && value.Maximum is null)
            throw new ResearchAssetRuleException("约束至少需要最小值或最大值。");
        if (value.Minimum > value.Maximum)
            throw new ResearchAssetRuleException("约束最小值不能大于最大值。");
        var severity = Required(value.Severity, "约束级别", 20).ToLowerInvariant();
        if (severity is not ("hard" or "soft"))
            throw new ResearchAssetRuleException("约束级别只能是 hard 或 soft。");
        var kind = Required(value.ConstraintKind, "约束类型", 80).ToLowerInvariant();
        if (!ConstraintKinds.Contains(kind))
            throw new ResearchAssetRuleException("约束类型只能是 range、safe-range 或 preferred-range。");
        return value with
        {
            ConstraintId = value.ConstraintId == Guid.Empty ? Guid.CreateVersion7() : value.ConstraintId,
            VariableCode = Required(value.VariableCode, "约束变量", 200).ToLowerInvariant(),
            ConstraintKind = kind,
            Unit = NormalizeUnit(value.Unit),
            Severity = severity
        };
    }

    private static MechanismForbiddenCombination NormalizeForbiddenCombination(
        MechanismForbiddenCombination value)
    {
        var factors = value.Factors.Select(factor =>
        {
            if (factor.Minimum is null && factor.Maximum is null)
                throw new ResearchAssetRuleException("禁止组合的每个因子至少需要最小值或最大值。");
            if (factor.Minimum > factor.Maximum)
                throw new ResearchAssetRuleException("禁止组合因子的最小值不能大于最大值。");
            return factor with
            {
                VariableCode = Required(factor.VariableCode, "禁止组合变量", 200).ToLowerInvariant(),
                Unit = NormalizeUnit(factor.Unit)
            };
        }).ToArray();
        if (factors.Length < 2)
            throw new ResearchAssetRuleException("禁止组合至少需要两个变量条件。");
        if (factors.Select(static item => item.VariableCode).Distinct(StringComparer.Ordinal).Count() != factors.Length)
            throw new ResearchAssetRuleException("同一禁止组合不能重复引用变量。");
        return value with
        {
            CombinationId = Guid.CreateVersion7(),
            Name = Required(value.Name, "禁止组合名称", 240),
            Factors = factors
        };
    }

    private static MechanismClaimEvidence NormalizeEvidence(MechanismClaimEvidence value)
    {
        var polarity = Required(value.Polarity, "证据方向", 20).ToLowerInvariant();
        if (polarity is not ("supporting" or "opposing"))
            throw new ResearchAssetRuleException("证据方向只能是 supporting 或 opposing。");
        return value with
        {
            EvidenceLinkId = value.EvidenceLinkId == Guid.Empty ? Guid.CreateVersion7() : value.EvidenceLinkId,
            EvidenceKind = Required(value.EvidenceKind, "证据类型", 80).ToLowerInvariant(),
            ReferenceId = Required(value.ReferenceId, "证据引用", 500),
            Polarity = polarity,
            ContentHash = NormalizeHash(value.ContentHash)
        };
    }

    private static string NormalizeHash(string? value)
    {
        var hash = Required(value, "证据哈希", 64).ToLowerInvariant();
        if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new ResearchAssetRuleException("证据哈希必须是 64 位 SHA-256。");
        return hash;
    }

    private static string Required(string? value, string name, int maximum)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ResearchAssetRuleException($"{name}不能为空且最长 {maximum} 个字符。");
        return value;
    }

    private static string NormalizeDimension(string? value)
    {
        var normalized = Required(value, "适用维度", 100).ToLowerInvariant();
        if (!ApplicabilityDimensions.Contains(normalized))
            throw new ResearchAssetRuleException("适用维度必须引用项目、过程、产品、材料、设备、工装、工艺规范、阶段或站点代码。");
        return normalized;
    }

    internal static string NormalizeUnit(string? value)
        => ProcessUnitConverter.NormalizeCode(Required(value, "单位", 80));

    private static void ValidateProjectBindings(
        ResearchProject project,
        IReadOnlyList<MechanismClaimVariable> variables,
        IReadOnlyList<MechanismClaimConstraint> constraints,
        IReadOnlyList<MechanismForbiddenCombination> forbiddenCombinations,
        IReadOnlyList<MechanismClaimApplicability> applicability)
    {
        var projectVariables = project.Variables.ToDictionary(static value => value.Code, StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (!projectVariables.TryGetValue(variable.VariableCode, out var projectVariable))
                throw new ResearchAssetRuleException($"机理变量 {variable.VariableCode} 未绑定当前研发项目变量。");
            if (!string.Equals(NormalizeUnit(projectVariable.Unit), variable.Unit, StringComparison.Ordinal))
                throw new ResearchAssetRuleException($"机理变量 {variable.VariableCode} 的单位与项目变量不一致。");
        }
        foreach (var constraint in constraints)
        {
            if (!projectVariables.TryGetValue(constraint.VariableCode, out var projectVariable) ||
                projectVariable.Role != ResearchVariableRoles.Control)
                throw new ResearchAssetRuleException($"机理约束 {constraint.VariableCode} 必须绑定当前项目可控变量。");
            if (!string.Equals(NormalizeUnit(projectVariable.Unit), constraint.Unit, StringComparison.Ordinal))
                throw new ResearchAssetRuleException($"机理约束 {constraint.VariableCode} 的单位与项目变量不一致。");
        }
        foreach (var combination in forbiddenCombinations)
        {
            var coversAllReferencedRanges = true;
            foreach (var factor in combination.Factors)
            {
                if (!projectVariables.TryGetValue(factor.VariableCode, out var projectVariable) ||
                    projectVariable.Role != ResearchVariableRoles.Control)
                    throw new ResearchAssetRuleException($"禁止组合变量 {factor.VariableCode} 必须绑定当前项目可控变量。");
                if (!string.Equals(NormalizeUnit(projectVariable.Unit), factor.Unit, StringComparison.Ordinal))
                    throw new ResearchAssetRuleException($"禁止组合变量 {factor.VariableCode} 的单位与项目变量不一致。");
                if (projectVariable.LowerLimit is { } lower && factor.Maximum is { } maximum && maximum < lower ||
                    projectVariable.UpperLimit is { } upper && factor.Minimum is { } minimum && minimum > upper)
                    throw new ResearchAssetRuleException($"禁止组合变量 {factor.VariableCode} 与项目工艺范围没有交集。");
                coversAllReferencedRanges &=
                    projectVariable.LowerLimit is { } projectLower &&
                    projectVariable.UpperLimit is { } projectUpper &&
                    (factor.Minimum is null || factor.Minimum <= projectLower) &&
                    (factor.Maximum is null || factor.Maximum >= projectUpper);
            }
            if (coversAllReferencedRanges)
                throw new ResearchAssetRuleException($"禁止组合 {combination.Name} 会排除整个项目工艺空间。");
        }
        var context = new Dictionary<string, string>(project.Context, StringComparer.OrdinalIgnoreCase)
        {
            ["project-code"] = project.Code,
            ["process"] = project.ProcessName
        };
        AddContext(context, "product", project.ProductName);
        AddContext(context, "material", project.MaterialName);
        AddContext(context, "site", project.SiteCode);
        foreach (var scope in applicability)
        {
            if (!context.TryGetValue(scope.DimensionCode, out var actual) ||
                !string.Equals(actual, scope.DimensionValue, StringComparison.OrdinalIgnoreCase))
                throw new ResearchAssetRuleException(
                    $"适用范围 {scope.DimensionCode}={scope.DimensionValue} 未绑定当前项目上下文。");
        }
    }

    private static void AddContext(IDictionary<string, string> context, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) context[key] = value;
    }

    private static string? Optional(string? value, int maximum)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > maximum)
            throw new ResearchAssetRuleException($"文本最长 {maximum} 个字符。");
        return value;
    }

    private static string ComputeHash(MechanismClaimVersion value)
    {
        var canonical = value with { ContentHash = "", ReviewedBy = null, ReviewedAt = null };
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }
}
