// 定义真实生产运行进入优化观察的应用边界。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed record ResearchObservationAssembly(
    IReadOnlyList<ResearchRunObservation> Observations,
    int CandidateRunCount)
{
    public int ValidObservationCount =>
        Observations.Count(static value => value.ValidForOptimization);
}

/// <summary>把跨模块运行与质量证据转换为工艺优化可消费的冻结观察。</summary>
public interface IResearchObservationAssembler
{
    /// <summary>
    /// 直接从项目适用范围内的已完成生产运行装配优化观察；不要求用户先创建验证计划。
    /// </summary>
    Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
        ResearchProject project,
        CancellationToken ct = default);

    /// <summary>
    /// 为一条已关联的真实生产运行装配证据，保持建议、实际运行和质量结果的可追溯关系。
    /// </summary>
    Task<ResearchObservationAssembly> AssembleProductionRunAsync(
        ResearchProject project,
        string executionKey,
        CancellationToken ct = default);

}
