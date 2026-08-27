// 从受控验证结果物化候选工艺操作域，并保持独立验证边界。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>把满足证据门槛的受控验证结果转为候选工艺操作域。</summary>
public sealed class ResearchOperatingRegionMaterializer(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow)
{
    public async Task<ResearchOperatingRegion?> MaterializeCandidateAsync(
        ResearchProject project,
        ResearchExperiment experiment,
        ResearchExperimentResult result,
        string userId,
        CancellationToken ct = default)
    {
        if (experiment.ValidationOperatingRegionId is { } validationOperatingRegionId)
        {
            return await workflow.AttachOperatingRegionValidationResultAsync(
                validationOperatingRegionId,
                experiment,
                result,
                userId,
                ct).ConfigureAwait(false);
        }
        if (experiment.DesignMethod != ResearchDesignMethods.BayesianOptimization ||
            experiment.Optimization is null || !result.SafetyPassed)
            return null;
        var existing = await store.ListOperatingRegionsAsync(project.ProjectId, ct)
            .ConfigureAwait(false);
        if (existing.Any(value => value.SupportingResultIds.Contains(result.ResultId)))
            return null;

        var predictions = experiment.Optimization.RunPredictions
            .ToDictionary(static value => value.ExecutionKey, StringComparer.Ordinal);
        var runPlan = experiment.RunPlan.ToDictionary(
            static value => value.ExecutionKey,
            StringComparer.Ordinal);
        var acceptedGroups = result.RunObservations
            .Where(static value => value.ValidForOptimization)
            .Where(value => MeetsMeasuredSpecification(project, value))
            .Where(value => !predictions.TryGetValue(value.ExecutionKey, out var prediction) ||
                            MeetsPredictedSpecification(project, prediction))
            .Where(value => runPlan.ContainsKey(value.ExecutionKey))
            .GroupBy(
                value => runPlan[value.ExecutionKey].ReplicateKey ?? runPlan[value.ExecutionKey].ExecutionKey,
                StringComparer.Ordinal)
            .Where(static group => group.Count() >= 2)
            .Select(group => new
            {
                Observations = group.ToArray(),
                ConservativePrediction = group
                    .Select(value => predictions.GetValueOrDefault(value.ExecutionKey)?.FeasibilityProbability)
                    .Where(static value => value.HasValue)
                    .Select(static value => value!.Value)
                    .DefaultIfEmpty(0)
                    .Min()
            })
            .OrderByDescending(static value => value.ConservativePrediction)
            .ThenByDescending(static value => value.Observations.Length)
            .ToArray();
        var accepted = acceptedGroups.FirstOrDefault();
        if (accepted is null)
            return null;

        var variables = new List<OperatingRegionVariable>();
        foreach (var control in project.Variables.Where(
                     static value => value.Role == ResearchVariableRoles.Control))
        {
            var values = accepted.Observations
                .SelectMany(static value => value.ActualFactors)
                .Where(value => value.VariableCode == control.Code)
                .Select(static value => value.Value)
                .Order()
                .ToArray();
            if (values.Length != accepted.Observations.Length)
                return null;
            var centre = Median(values);
            variables.Add(new OperatingRegionVariable
            {
                VariableCode = control.Code,
                LowerBound = centre,
                UpperBound = centre,
                Unit = control.Unit
            });
        }

        var empiricalConfidence = WilsonLowerBound(
            accepted.Observations.Length,
            accepted.Observations.Length);
        var posteriorConfidence = accepted.ConservativePrediction > 0
            ? Math.Min(empiricalConfidence, accepted.ConservativePrediction)
            : empiricalConfidence;
        return await workflow.SaveOperatingRegionAsync(
            project.ProjectId,
            new ResearchOperatingRegion
            {
                Name = $"自动候选操作域 · {experiment.Name}",
                Variables = variables,
                ObjectiveCodes = experiment.ObjectiveCodes,
                SupportingExperimentIds = [experiment.ExperimentId],
                SupportingResultIds = [result.ResultId],
                Confidence = Math.Clamp(posteriorConfidence, 0.01, 0.999),
                ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
                AnalysisRunId = result.AnalysisRunId,
                AnalysisHash = result.AnalysisHash,
                Applicability = BuildApplicability(project, accepted.Observations.Length, result.RunCount)
            },
            userId,
            ct).ConfigureAwait(false);
    }

    private static bool MeetsMeasuredSpecification(
        ResearchProject project,
        ResearchRunObservation observation)
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
            "target" => false,
            _ => false
        };

    private static double Median(IReadOnlyList<double> ordered)
    {
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

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
        return $"{string.Join(" / ", scope)}；依据同一候选条件下 {acceptedRuns}/{totalRuns} 个已执行且达标运行形成。" +
               "当前仅代表一个经重复实测的候选设置，不外推为连续安全区域；投入生产前必须完成独立跨区组验证。";
    }
}
