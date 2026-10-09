// 管理按站点与配方隔离的机理知识版本、审核、冲突和证据升级。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>
/// 机理声明属于一个站点下的一个配方；变量与约束只能引用该配方已发布数据模型中的参数，
/// 不反向编排配方优化工作流。
/// </summary>
public sealed class MechanismKnowledgeService(
    IMechanismKnowledgeStore store,
    RecipeKnowledgeScopeReader scopes)
{
    public const string SiteDimension = "site";
    public const string ProcessSpecificationDimension = "process-specification";

    private static readonly IReadOnlySet<string> ApplicabilityDimensions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SiteDimension, ProcessSpecificationDimension, "product", "equipment"
        };
    private static readonly IReadOnlySet<string> VariableRoles =
        new HashSet<string>(["cause", "mediator", "outcome", "moderator"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Directions =
        new HashSet<string>(["increase", "decrease", "nonlinear"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ConstraintKinds =
        new HashSet<string>(["range", "safe-range", "preferred-range"], StringComparer.Ordinal);

    public Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default)
        => store.ListClaimsAsync(siteCode, processSpecificationId, ct);

    public Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default)
        => store.ListConflictsAsync(siteCode, processSpecificationId, ct);

    public async Task<MechanismClaimVersion> SaveDraftAsync(
        MechanismClaimVersion request,
        string userId,
        CancellationToken ct = default)
    {
        var actor = Required(userId, "创建人", 200);
        var existing = request.ClaimId == Guid.Empty
            ? null
            : await store.GetClaimAsync(request.ClaimId, null, ct).ConfigureAwait(false);
        var scope = await scopes.ReadAsync(
            existing?.SiteCode ?? request.SiteCode,
            existing?.ProcessSpecificationId ?? request.ProcessSpecificationId,
            ct).ConfigureAwait(false);
        if (existing is not null &&
            (!string.Equals(request.SiteCode?.Trim(), existing.SiteCode, StringComparison.Ordinal) ||
             !string.Equals(request.ProcessSpecificationId?.Trim(), existing.ProcessSpecificationId,
                 StringComparison.Ordinal)))
            throw new ResearchAssetRuleException("机理声明所属的站点和配方不能更改。");
        if (existing is not null && existing.Status != MechanismClaimStatuses.Draft)
            throw new ResearchAssetRuleException("已进入审核流程的声明不可覆盖，请创建新的机理声明。");

        var type = Required(request.MechanismType, "机理类型", 80).ToLowerInvariant();
        if (!MechanismClaimTypes.All.Contains(type))
            throw new ResearchAssetRuleException("机理类型无效。");
        var variables = request.Variables.Select(NormalizeVariable).DistinctBy(
            value => (value.VariableCode, value.VariableRole)).ToArray();
        if (variables.Length == 0)
            throw new ResearchAssetRuleException("机理声明至少需要一个变量。");
        var applicability = NormalizeApplicability(scope, request.Applicability);
        var constraints = request.Constraints.Select(NormalizeConstraint).ToArray();
        var forbiddenCombinations = request.ForbiddenCombinations
            .Select(NormalizeForbiddenCombination).ToArray();
        var evidence = request.Evidence.Select(NormalizeEvidence).DistinctBy(
            value => (value.EvidenceKind, value.ReferenceId, value.Polarity)).ToArray();
        if (evidence.Length == 0)
            throw new ResearchAssetRuleException("机理声明至少需要一个可追溯证据引用。");
        foreach (var item in evidence)
            if (!await store.EvidenceExistsAsync(scope.SiteCode, scope.ProcessSpecificationId, item, ct)
                    .ConfigureAwait(false))
                throw new ResearchAssetRuleException("证据引用不存在、不属于当前站点和配方，或内容哈希不匹配。");
        ValidateRecipeBindings(scope, variables, constraints, forbiddenCombinations);

        var now = DateTimeOffset.UtcNow;
        var claimId = existing?.ClaimId ?? (request.ClaimId == Guid.Empty ? Guid.CreateVersion7() : request.ClaimId);
        var version = existing is null ? 1 : existing.Version + 1;
        var value = new MechanismClaimVersion
        {
            ClaimId = claimId,
            SiteCode = scope.SiteCode,
            ProcessSpecificationId = scope.ProcessSpecificationId,
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
        MechanismClaimConflictRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.LeftClaimId == request.RightClaimId)
            throw new ResearchAssetRuleException("冲突两侧必须是不同声明。");
        var left = await store.GetClaimAsync(request.LeftClaimId, request.LeftClaimVersion, ct).ConfigureAwait(false);
        var right = await store.GetClaimAsync(request.RightClaimId, request.RightClaimVersion, ct).ConfigureAwait(false);
        if (left is null || right is null ||
            !string.Equals(left.SiteCode, right.SiteCode, StringComparison.Ordinal) ||
            !string.Equals(left.ProcessSpecificationId, right.ProcessSpecificationId, StringComparison.Ordinal))
            throw new ResearchAssetRuleException("冲突声明必须存在且属于同一站点和配方。");
        return await store.AddConflictAsync(new MechanismClaimConflict
        {
            ConflictId = Guid.CreateVersion7(),
            SiteCode = left.SiteCode,
            ProcessSpecificationId = left.ProcessSpecificationId,
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
        Guid conflictId,
        MechanismClaimConflictResolutionRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var conflict = await store.GetConflictAsync(conflictId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理冲突不存在。");
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
        Guid claimId,
        MechanismClaimLifecycleRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var claim = await store.GetClaimAsync(claimId, null, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理声明不存在。");
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
        string? evaluationOutcome = null;
        string? evaluationSummary = null;
        if (target is MechanismClaimStatuses.Supported or MechanismClaimStatuses.Validated || isFalsification)
        {
            evidenceKind = Required(request.EvidenceKind, "验证证据类型", 80).ToLowerInvariant();
            if (evidenceKind != "recipe-recommendation-outcome")
                throw new ResearchAssetRuleException("支持和验证升级必须引用已冻结的配方建议实际运行结果。");
            referenceId = Required(request.ReferenceId, "配方建议决定引用", 500);
            contentHash = Required(request.ContentHash, "真实运行结果哈希", 64).ToLowerInvariant();
            if (!Regex.IsMatch(contentHash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                throw new ResearchAssetRuleException("真实运行结果哈希必须是 64 位 SHA-256。");
            evaluationOutcome = Required(request.EvaluationOutcome, "证据评价结论", 20).ToLowerInvariant();
            var expectedOutcome = isFalsification ? "falsifies" : "supports";
            if (evaluationOutcome != expectedOutcome)
                throw new ResearchAssetRuleException(isFalsification
                    ? "反证声明必须引用明确不满足预期效应的真实运行结果。"
                    : "只有明确支持声明的真实运行结果评价才能升级。");
            evaluationSummary = Required(request.EvaluationSummary, "证据评价说明", 4000);
            var evidence = new MechanismClaimEvidence
            {
                EvidenceKind = evidenceKind,
                ReferenceId = referenceId,
                ContentHash = contentHash
            };
            if (!await store.RecipeRecommendationOutcomeSupportsClaimAsync(claim, evidence, ct).ConfigureAwait(false))
                throw new ResearchAssetRuleException(
                    "真实运行结果必须来自同一站点和配方下已完成的配方建议闭环，并通过源数据校验。");
            if (await store.LifecycleEvidenceUsedAsync(claimId, referenceId, ct).ConfigureAwait(false))
                throw new ResearchAssetRuleException("同一真实运行结果不能重复用于机理知识升级。");
        }
        if (target == MechanismClaimStatuses.Active)
        {
            var hasOpenConflict = (await store.ListConflictsAsync(
                    claim.SiteCode, claim.ProcessSpecificationId, ct).ConfigureAwait(false))
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
            EvaluationOutcome = evaluationOutcome,
            EvaluationSummary = evaluationSummary,
            Comment = Optional(request.Comment, 4000),
            DecidedBy = actor,
            DecidedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<MechanismClaimApplicability> NormalizeApplicability(
        RecipeKnowledgeScope scope,
        IReadOnlyList<MechanismClaimApplicability> source)
    {
        var values = source.Select(value => new MechanismClaimApplicability
        {
            DimensionCode = NormalizeDimension(value.DimensionCode),
            DimensionValue = Required(value.DimensionValue, "适用实体代码", 300).ToLowerInvariant()
        }).ToList();
        foreach (var (dimension, expected) in new[]
                 {
                     (SiteDimension, scope.SiteCode),
                     (ProcessSpecificationDimension, scope.ProcessSpecificationId)
                 })
        {
            if (values.Any(value => value.DimensionCode == dimension &&
                    !string.Equals(value.DimensionValue, expected, StringComparison.OrdinalIgnoreCase)))
                throw new ResearchAssetRuleException($"适用范围 {dimension} 必须等于声明所属的站点或配方。");
            values.Add(new MechanismClaimApplicability
            {
                DimensionCode = dimension,
                DimensionValue = expected.ToLowerInvariant()
            });
        }
        return values.DistinctBy(value => (value.DimensionCode, value.DimensionValue))
            .OrderBy(static value => value.DimensionCode, StringComparer.Ordinal)
            .ThenBy(static value => value.DimensionValue, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateRecipeBindings(
        RecipeKnowledgeScope scope,
        IReadOnlyList<MechanismClaimVariable> variables,
        IReadOnlyList<MechanismClaimConstraint> constraints,
        IReadOnlyList<MechanismForbiddenCombination> forbiddenCombinations)
    {
        foreach (var variable in variables.Where(static value => value.VariableRole != "outcome"))
        {
            var unit = scope.ControlParameters.TryGetValue(variable.VariableCode, out var parameter)
                ? parameter.Unit
                : scope.DataItems.TryGetValue(variable.VariableCode, out var item)
                    ? item.Unit
                    : throw new ResearchAssetRuleException(
                        $"机理变量 {variable.VariableCode} 不在配方 {scope.ProcessSpecificationId} 的参数或采集项中。");
            RequireUnit(variable.VariableCode, unit, variable.Unit, "机理变量");
        }
        foreach (var constraint in constraints)
        {
            var parameter = RequireAdjustableParameter(scope, constraint.VariableCode, "机理约束");
            RequireUnit(constraint.VariableCode, parameter.Unit, constraint.Unit, "机理约束");
        }
        foreach (var combination in forbiddenCombinations)
        {
            var coversAllReferencedRanges = true;
            foreach (var factor in combination.Factors)
            {
                var parameter = RequireAdjustableParameter(scope, factor.VariableCode, "禁止组合变量");
                RequireUnit(factor.VariableCode, parameter.Unit, factor.Unit, "禁止组合变量");
                if (parameter.Minimum is { } lower && factor.Maximum is { } maximum && maximum < lower ||
                    parameter.Maximum is { } upper && factor.Minimum is { } minimum && minimum > upper)
                    throw new ResearchAssetRuleException($"禁止组合变量 {factor.VariableCode} 与配方参数范围没有交集。");
                coversAllReferencedRanges &=
                    parameter.Minimum is { } parameterLower &&
                    parameter.Maximum is { } parameterUpper &&
                    (factor.Minimum is null || factor.Minimum <= parameterLower) &&
                    (factor.Maximum is null || factor.Maximum >= parameterUpper);
            }
            if (coversAllReferencedRanges)
                throw new ResearchAssetRuleException($"禁止组合 {combination.Name} 会排除整个配方参数空间。");
        }
    }

    private static Ingot.Contracts.ProcessConfiguration.ControlParameterDefinition RequireAdjustableParameter(
        RecipeKnowledgeScope scope,
        string code,
        string label)
    {
        if (!scope.ControlParameters.TryGetValue(code, out var parameter) || !parameter.ChangeAllowed)
            throw new ResearchAssetRuleException($"{label} {code} 必须是配方中允许调整的参数。");
        return parameter;
    }

    private static void RequireUnit(string code, string? expected, string actual, string label)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(NormalizeUnit(expected), actual, StringComparison.Ordinal))
            throw new ResearchAssetRuleException($"{label} {code} 的单位与配方定义不一致。");
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
            throw new ResearchAssetRuleException("适用维度只能是站点、配方、产品或设备。");
        return normalized;
    }

    internal static string NormalizeUnit(string? value)
        => ProcessUnitConverter.NormalizeCode(Required(value, "单位", 80));

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
