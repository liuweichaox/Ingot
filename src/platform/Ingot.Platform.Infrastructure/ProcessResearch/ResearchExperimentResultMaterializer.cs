using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     将已经完成采集和检验的运行观察固化为正式实验结果。它只处理处于 Running、
///     尚无结果且全部计划运行均已形成有效观察的实验，因此不会替代工程师的启动审批。
/// </summary>
public sealed class ResearchExperimentResultMaterializer(
    ProcessResearchWorkflow workflow)
{
    public async Task<IReadOnlyList<ResearchExperimentResult>> MaterializeCompletedAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        IReadOnlyList<ResearchExperimentResult> existingResults,
        ResearchObservationAssembly assembly,
        string userId,
        CancellationToken ct = default)
    {
        var existingExperimentIds = existingResults
            .Select(static value => value.ExperimentId)
            .ToHashSet();
        var observationsByRun = assembly.Observations
            .Where(static value => value.ValidForOptimization)
            .ToDictionary(static value => value.RunKey, StringComparer.Ordinal);
        var priorObservations = existingResults
            .SelectMany(static value => value.RunObservations)
            .Where(static value => value.ValidForOptimization)
            .ToArray();
        var created = new List<ResearchExperimentResult>();
        foreach (var experiment in experiments
                     .Where(value =>
                         value.Status == ResearchExperimentStatuses.Running &&
                         !existingExperimentIds.Contains(value.ExperimentId))
                     .OrderBy(static value => value.CreatedAt))
        {
            var observations = experiment.RunPlan
                .Select(run => observationsByRun.GetValueOrDefault(run.RunKey))
                .ToArray();
            if (observations.Any(static value => value is null))
                continue;
            var resolved = observations.Select(static value => value!).ToArray();
            var snapshotHash = Convert.ToHexStringLower(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    resolved.Select(static value => new
                    {
                        value.RunKey,
                        value.SourceContentHash
                    }))));
            var result = await workflow.RecordExperimentResultAsync(
                experiment.ExperimentId,
                new ResearchExperimentResult
                {
                    DatasetSnapshotId = $"cycle-observation-snapshot:{snapshotHash}",
                    Metrics = BuildMetrics(project, resolved, priorObservations),
                    RunObservations = resolved,
                    RunCount = resolved.Length,
                    ReplicateCount = Math.Max(
                        1,
                        experiment.RunPlan
                            .Select(static value => value.ReplicateKey)
                            .Where(static value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.Ordinal)
                            .Count()),
                    DistinctMaterialLotCount = 1,
                    DistinctEquipmentCount = 1,
                    SafetyPassed = SatisfiesOutcomeConstraints(project, resolved),
                    CalculatedFromSource = true
                },
                userId,
                ct).ConfigureAwait(false);
            created.Add(result);
            priorObservations = priorObservations.Concat(resolved).ToArray();
        }
        return created;
    }

    private static IReadOnlyList<ExperimentMetricResult> BuildMetrics(
        ResearchProject project,
        IReadOnlyList<ExperimentRunObservation> observations,
        IReadOnlyList<ExperimentRunObservation> prior)
        => project.Objectives.Select(objective =>
        {
            var values = observations.Select(value => value.Outcomes[objective.Code]).ToArray();
            var previous = prior
                .Where(value => value.Outcomes.ContainsKey(objective.Code))
                .Select(value => value.Outcomes[objective.Code])
                .ToArray();
            var observed = values.Average();
            var baseline = previous.Length > 0
                ? previous.Average()
                : objective.Baseline ?? observed;
            var standardError = values.Length < 2
                ? 0
                : StandardDeviation(values) / Math.Sqrt(values.Length);
            return new ExperimentMetricResult
            {
                ObjectiveCode = objective.Code,
                BaselineValue = baseline,
                ObservedValue = observed,
                EffectValue = observed - baseline,
                LowerConfidenceBound = observed - 1.96 * standardError,
                UpperConfidenceBound = observed + 1.96 * standardError,
                Unit = objective.Unit,
                BaselineSampleCount = Math.Max(1, previous.Length),
                ExperimentSampleCount = values.Length,
                ComputationMethod = "cycle-observation-mean-and-normal-95ci-v1"
            };
        }).ToArray();

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(
            values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1));
    }

    private static bool SatisfiesOutcomeConstraints(
        ResearchProject project,
        IReadOnlyList<ExperimentRunObservation> observations)
        => project.OutcomeConstraints.All(constraint =>
            observations.All(observation =>
            {
                var value = observation.ConstraintOutcomes[constraint.Code];
                return constraint.Operator == "<="
                    ? value <= constraint.Limit
                    : value >= constraint.Limit;
            }));
}
