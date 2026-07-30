using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     从已经实际执行并完成检验的优化实验中形成“候选窗口”。这里只自动生成候选，
///     不替代独立复核、实验室重复验证或生产发布。
/// </summary>
public sealed class ResearchProcessWindowMaterializer(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow)
{
    public async Task<ResearchProcessWindow?> MaterializeCandidateAsync(
        ResearchProject project,
        ResearchExperiment experiment,
        ResearchExperimentResult result,
        string userId,
        CancellationToken ct = default)
    {
        if (experiment.DesignMethod != ResearchDesignMethods.BayesianOptimization ||
            experiment.Optimization is null || !result.SafetyPassed)
            return null;
        var existing = await store.ListProcessWindowsAsync(project.ProjectId, ct)
            .ConfigureAwait(false);
        if (existing.Any(value => value.SupportingResultIds.Contains(result.ResultId)))
            return null;

        var predictions = experiment.Optimization.RunPredictions
            .ToDictionary(static value => value.RunKey, StringComparer.Ordinal);
        var accepted = result.RunObservations
            .Where(static value => value.ValidForOptimization)
            .Where(value => MeetsMeasuredSpecification(project, value))
            .Where(value => !predictions.TryGetValue(value.RunKey, out var prediction) ||
                            MeetsPredictedSpecification(project, prediction))
            .ToArray();
        if (accepted.Length < 2)
            return null;

        var variables = new List<ProcessWindowVariable>();
        foreach (var control in project.Variables.Where(
                     static value => value.Role == ResearchVariableRoles.Control))
        {
            var values = accepted
                .SelectMany(static value => value.ActualFactors)
                .Where(value => value.VariableCode == control.Code)
                .Select(static value => value.Value)
                .Distinct()
                .Order()
                .ToArray();
            if (values.Length < 2 || values[0] >= values[^1])
                return null;
            variables.Add(new ProcessWindowVariable
            {
                VariableCode = control.Code,
                LowerBound = values[0],
                UpperBound = values[^1],
                Unit = control.Unit
            });
        }

        var posteriorConfidence = accepted
            .Select(value => predictions.GetValueOrDefault(value.RunKey)?.FeasibilityProbability)
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .DefaultIfEmpty(WilsonLowerBound(accepted.Length, result.RunObservations.Count))
            .Min();
        return await workflow.SaveProcessWindowAsync(
            project.ProjectId,
            new ResearchProcessWindow
            {
                Name = $"自动候选窗口 · {experiment.Name}",
                Variables = variables,
                ObjectiveCodes = experiment.ObjectiveCodes,
                SupportingExperimentIds = [experiment.ExperimentId],
                SupportingResultIds = [result.ResultId],
                Confidence = Math.Clamp(posteriorConfidence, 0.01, 0.999),
                ConfidenceMethod = ResearchConfidenceMethods.Bayesian,
                AnalysisRunId = result.AnalysisRunId,
                AnalysisHash = result.AnalysisHash,
                Applicability = BuildApplicability(project, accepted.Length, result.RunCount)
            },
            userId,
            ct).ConfigureAwait(false);
    }

    private static bool MeetsMeasuredSpecification(
        ResearchProject project,
        ExperimentRunObservation observation)
        => project.Objectives.All(objective =>
               observation.Outcomes.TryGetValue(objective.Code, out var value) &&
               MeetsObjective(objective, value, value)) &&
           project.OutcomeConstraints.All(constraint =>
               observation.ConstraintOutcomes.TryGetValue(constraint.Code, out var value) &&
               (constraint.Operator == "<=" ? value <= constraint.Limit : value >= constraint.Limit));

    private static bool MeetsPredictedSpecification(
        ResearchProject project,
        OptimizationRunPrediction prediction)
        => project.Objectives.All(objective =>
               prediction.Objectives.TryGetValue(objective.Code, out var estimate) &&
               MeetsObjective(objective, estimate.Lower95, estimate.Upper95)) &&
           project.OutcomeConstraints.All(constraint =>
               !constraint.SafetyCritical ||
               prediction.FeasibilityProbability is { } probability &&
               probability >= constraint.MinimumProbability);

    private static bool MeetsObjective(
        ResearchObjective objective,
        double lower,
        double upper)
        => objective.Direction switch
        {
            "minimize" => upper <= (objective.UpperLimit ?? objective.Target),
            "maximize" => lower >= (objective.LowerLimit ?? objective.Target),
            "range" => objective.LowerLimit is { } min && objective.UpperLimit is { } max &&
                       lower >= min && upper <= max,
            "target" when objective.LowerLimit is { } min && objective.UpperLimit is { } max =>
                lower >= min && upper <= max,
            "target" => lower <= objective.Target && upper >= objective.Target,
            _ => false
        };

    private static double WilsonLowerBound(int successCount, int totalCount)
    {
        if (totalCount <= 0)
            return 0.01;
        const double z = 1.96;
        var proportion = (double)successCount / totalCount;
        var denominator = 1 + z * z / totalCount;
        var centre = proportion + z * z / (2 * totalCount);
        var margin = z * Math.Sqrt(
            proportion * (1 - proportion) / totalCount +
            z * z / (4 * totalCount * totalCount));
        return Math.Max(0.01, (centre - margin) / denominator);
    }

    private static string BuildApplicability(
        ResearchProject project,
        int acceptedRuns,
        int totalRuns)
    {
        var scope = new[]
        {
            project.ProcessName,
            project.ProductName,
            project.MaterialName
        }.Where(static value => !string.IsNullOrWhiteSpace(value));
        return $"{string.Join(" / ", scope)}；依据 {acceptedRuns}/{totalRuns} 个已执行且达标运行形成。" +
               "当前范围仅是模型推荐并经实测覆盖的候选区间，投入生产前必须完成独立重复验证。";
    }
}
