namespace Ingot.Platform.Application.ProcessResearch;

/// 参数优化命令：核心优化流程
/// 这个服务在应用层，与基础设施层（存储）解耦
public interface IParameterOptimizationCommand
{
    Task<OptimizationRecommendation> OptimizeAsync(
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> trainingData,
        CancellationToken ct);
}

public sealed class ParameterOptimizationCommand : IParameterOptimizationCommand
{
    private readonly OptimizerFactory _optimizerFactory;
    private readonly IOptimizationExplainabilityEngine _explainabilityEngine;

    public ParameterOptimizationCommand(IOptimizationExplainabilityEngine explainabilityEngine)
    {
        _optimizerFactory = new OptimizerFactory();
        _explainabilityEngine = explainabilityEngine;
    }

    public async Task<OptimizationRecommendation> OptimizeAsync(
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> trainingData,
        CancellationToken ct)
    {
        // 选择优化算法求解
        var optimizer = _optimizerFactory.GetOptimizer(problem.PreferredAlgorithm);
        var recommendation = await optimizer.OptimizeAsync(problem, trainingData, ct);

        return recommendation;
    }
}
