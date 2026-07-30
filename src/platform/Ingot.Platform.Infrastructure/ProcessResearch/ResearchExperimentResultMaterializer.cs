using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     将已经完成采集和检验的运行观察固化为正式实验结果。它只处理处于 Running、
///     尚无结果且全部计划运行均已形成有效观察的实验，因此不会替代工程师的启动审批。
/// </summary>
public sealed class ResearchExperimentResultMaterializer(
    ProcessResearchWorkflow workflow,
    ResearchProcessWindowMaterializer? processWindowMaterializer = null,
    IProcessResearchStore? store = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ResearchExperimentResult>> MaterializeCompletedAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        IReadOnlyList<ResearchExperimentResult> existingResults,
        ResearchObservationAssembly assembly,
        string userId,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await MaterializeCoreAsync(
                project,
                experiments,
                existingResults,
                assembly,
                userId,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ResearchExperimentResult>> MaterializeCoreAsync(
        ResearchProject project,
        IReadOnlyList<ResearchExperiment> experiments,
        IReadOnlyList<ResearchExperimentResult> existingResults,
        ResearchObservationAssembly assembly,
        string userId,
        CancellationToken ct)
    {
        if (store is not null)
        {
            experiments = await store.ListExperimentsAsync(project.ProjectId, ct)
                .ConfigureAwait(false);
            existingResults = await store.ListExperimentResultsAsync(project.ProjectId, ct)
                .ConfigureAwait(false);
        }
        var existingExperimentIds = existingResults
            .Select(static value => value.ExperimentId)
            .ToHashSet();
        var observationsByRun = assembly.Observations
            .Where(static value => value.ValidForOptimization)
            .ToDictionary(static value => value.RunKey, StringComparer.Ordinal);
        var historicalOrCompletedRunKeys = experiments
            .Where(static value =>
                value.DesignMethod == ResearchDesignMethods.HistoricalObservation ||
                value.Status == ResearchExperimentStatuses.Completed)
            .SelectMany(static value => value.RunPlan)
            .Select(static value => value.RunKey)
            .ToHashSet(StringComparer.Ordinal);
        var priorObservations = assembly.Observations
            .Where(value =>
                value.ValidForOptimization &&
                historicalOrCompletedRunKeys.Contains(value.RunKey))
            .Concat(existingResults
            .SelectMany(static value => value.RunObservations)
            .Where(static value => value.ValidForOptimization))
            .DistinctBy(static value => value.RunKey, StringComparer.Ordinal)
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
                    SafetyPassed = SatisfiesOutcomeConstraints(project, resolved),
                    CalculatedFromSource = true
                },
                userId,
                ct).ConfigureAwait(false);
            created.Add(result);
            if (processWindowMaterializer is not null)
            {
                await processWindowMaterializer.MaterializeCandidateAsync(
                    project,
                    experiment,
                    result,
                    userId,
                    ct).ConfigureAwait(false);
            }
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
            var hasIndependentSamples = values.Length >= 2 && previous.Length >= 2;
            var effect = observed - baseline;
            var standardError = hasIndependentSamples
                ? Math.Sqrt(
                    Math.Pow(StandardDeviation(values), 2) / values.Length +
                    Math.Pow(StandardDeviation(previous), 2) / previous.Length)
                : double.NaN;
            var degreesOfFreedom = hasIndependentSamples
                ? WelchDegreesOfFreedom(values, previous)
                : double.NaN;
            var margin = hasIndependentSamples
                ? StudentT95Critical(degreesOfFreedom) * standardError
                : double.NaN;
            return new ExperimentMetricResult
            {
                ObjectiveCode = objective.Code,
                BaselineValue = baseline,
                ObservedValue = observed,
                EffectValue = effect,
                LowerConfidenceBound = hasIndependentSamples ? effect - margin : null,
                UpperConfidenceBound = hasIndependentSamples ? effect + margin : null,
                Unit = objective.Unit,
                BaselineSampleCount = Math.Max(1, previous.Length),
                ExperimentSampleCount = values.Length,
                ComputationMethod = hasIndependentSamples
                    ? "two-sample-welch-effect-95ci-v2"
                    : "descriptive-effect-no-independent-control-v2"
            };
        }).ToArray();

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(
            values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1));
    }

    private static double WelchDegreesOfFreedom(
        IReadOnlyList<double> experiment,
        IReadOnlyList<double> baseline)
    {
        var experimentTerm = Math.Pow(StandardDeviation(experiment), 2) / experiment.Count;
        var baselineTerm = Math.Pow(StandardDeviation(baseline), 2) / baseline.Count;
        var denominator =
            Math.Pow(experimentTerm, 2) / (experiment.Count - 1) +
            Math.Pow(baselineTerm, 2) / (baseline.Count - 1);
        return denominator <= 0
            ? experiment.Count + baseline.Count - 2
            : Math.Pow(experimentTerm + baselineTerm, 2) / denominator;
    }

    // 小样本使用双侧 95% t 临界值表，并向下取整自由度以保持保守；
    // 30 以上才使用收敛良好的 Cornish-Fisher 近似。
    private static double StudentT95Critical(double degreesOfFreedom)
    {
        if (!double.IsFinite(degreesOfFreedom) || degreesOfFreedom <= 0)
            return double.PositiveInfinity;
        double[] smallSampleCriticalValues =
        [
            12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228,
            2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086,
            2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042
        ];
        if (degreesOfFreedom <= smallSampleCriticalValues.Length)
        {
            var index = Math.Max(1, (int)Math.Floor(degreesOfFreedom)) - 1;
            return smallSampleCriticalValues[index];
        }
        const double z = 1.959963984540054;
        var df = degreesOfFreedom;
        return z +
               (Math.Pow(z, 3) + z) / (4 * df) +
               (5 * Math.Pow(z, 5) + 16 * Math.Pow(z, 3) + 3 * z) /
               (96 * df * df);
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
