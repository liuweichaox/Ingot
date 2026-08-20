using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>为实验命令工作流提供受乐观并发保护的状态持久化。</summary>
public interface IResearchExperimentCommandStore
{
    Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ResearchHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken ct = default);
    Task<ResearchExperiment?> GetExperimentAsync(Guid experimentId, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
        Guid projectId,
        CancellationToken ct = default);
    Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
    Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default);
}

/// <summary>在保存或推进实验前校验运行计划和项目定义的一致性。</summary>
public interface IResearchExperimentPlanValidator
{
    Task<ResearchExperimentValidationResult> ValidateAsync(
        Guid projectId,
        ResearchExperiment request,
        CancellationToken ct = default);
}

/// <summary>以封闭式规则判断实验是否具备受控在线建议资格。</summary>
public interface IResearchOnlineAdmissionGate
{
    Task<ResearchOnlineAdmissionEvidence> RequireAsync(
        Guid projectId,
        string mechanismKnowledgeSnapshotHash,
        CancellationToken ct = default);
}

/// <summary>验证实验计划没有违反当前冻结的机理知识约束。</summary>
public interface IResearchExperimentKnowledgeGate
{
    Task ValidateAsync(ResearchExperiment experiment, CancellationToken ct = default);
}

/// <summary>表示研发项目或实验命令违反了业务规则。</summary>
public sealed class ProcessResearchRuleException(string message)
    : InvalidOperationException(message);

/// <summary>汇总实验计划中可向用户展示的结构化校验错误。</summary>
public sealed class ResearchExperimentValidationException(
    IReadOnlyList<ResearchExperimentValidationIssue> errors)
    : InvalidOperationException(errors.FirstOrDefault()?.Message ?? "实验计划未通过校验。")
{
    public IReadOnlyList<ResearchExperimentValidationIssue> Errors { get; } = errors;
}
