namespace Ingot.Contracts.Insight;

/// <summary>
///     证据阶梯等级。L0–L2.5 由数据自动评定；L3+ 为锁定等级，仅显示解锁条件，不自动晋级。
///     等级是"某个具名问题上的证据状态"，不是软件模块是否存在。
/// </summary>
public static class CaseLevels
{
    public const string L0Pending = "L0-pending";   // 生产记录尚不达标
    public const string L0 = "L0";                   // 生产记录可用
    public const string L1 = "L1";                   // 生产过程看得清
    public const string L2 = "L2";                   // 参数关系找得到
    public const string L2_5 = "L2.5";               // 过程稳定可监控（需监控规则上线）
    public const string L3 = "L3";                   // 质量风险可预判（锁定：需 ≥300 带标签周期）
    public const string L4 = "L4";                   // 参数影响可确认（锁定：需受控试验）
    public const string L5 = "L5";                   // 工艺优化可建议（锁定：需 confirmed 结论）

    public static readonly IReadOnlyList<string> AutoGradable = [L0, L1, L2];
}

/// <summary>问题档案绑定范围：解析为 production_events 的 subject 与 context 过滤 + 时间窗。</summary>
public sealed record CaseScope
{
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }

    /// <summary>context 键值过滤（如 mold_id=MOLD-02）。以 JSONB 包含匹配，与事件查询一致。</summary>
    public IReadOnlyDictionary<string, string> ContextFilter { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>L2 同类分组的 context 键（如 mold_id）；缺省表示范围本身已固定同类维度。</summary>
    public string? ComparisonKey { get; init; }

    public DateTimeOffset? WindowFrom { get; init; }
    public DateTimeOffset? WindowTo { get; init; }
}

public sealed record ProblemCase
{
    public required Guid CaseId { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "open";
    public required CaseScope Scope { get; init; }
    public string TargetMetric { get; init; } = string.Empty;
    public string CurrentLevel { get; init; } = CaseLevels.L0Pending;

    /// <summary>L2 人工门：特征集经工艺工程师核定。只有工艺工程师/管理员可置位。</summary>
    public bool FeatureSetRatified { get; init; }
    public string? RatifiedBy { get; init; }
    public DateTimeOffset? RatifiedAt { get; init; }

    public string? Owner { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>单条晋级门槛的证据：测量值、阈值、是否通过。UI 用它展示"还差什么"。</summary>
public sealed record LevelGate
{
    public required string Name { get; init; }
    public required string Tier { get; init; }       // 该门槛所属等级 L0/L1/L2
    public required double Measured { get; init; }
    public required double Threshold { get; init; }
    public required string Comparator { get; init; } // ">=" | "<" | "=="
    public required bool Passed { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed record LevelEvaluation
{
    public required Guid CaseId { get; init; }
    public required string Level { get; init; }
    public required IReadOnlyList<LevelGate> Gates { get; init; }
    public required int WindowDays { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>下一级尚未满足的门槛（供 UI 直接显示晋级缺口）。</summary>
    public IReadOnlyList<LevelGate> UnmetGates => Gates.Where(static gate => !gate.Passed).ToArray();
}

public sealed record ProblemCaseUpsertRequest
{
    public Guid? CaseId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
    public CaseScope? Scope { get; init; }
    public string? TargetMetric { get; init; }
    public string? Owner { get; init; }
}
