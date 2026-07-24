using Ingot.Contracts.ProcessImprovement;
using Ingot.Platform.Infrastructure.ProcessImprovement;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ScientificTrialResultCalculatorTests
{
    [Fact]
    public async Task Calculate_uses_versioned_cycle_features_and_produces_stable_evidence()
    {
        var trial = ConfirmatoryTrial();
        var source = new MemoryEvidenceSource(
            trial.ControlCycleIds.Select((id, index) => Observation(id, 10 + index))
                .Concat(trial.TrialCycleIds.Select((id, index) => Observation(id, 8 + index)))
                .ToArray(),
            trial.TrialCycleIds.Select(id => Observation(id, 180)).ToArray());
        var calculator = new ScientificTrialResultCalculator(source);

        var result = await calculator.CalculateAsync(trial, "scientist-a");
        var repeated = await calculator.CalculateAsync(trial, "scientist-a");

        Assert.Equal(14.5, result.BaselineValue);
        Assert.Equal(12.5, result.TrialValue);
        Assert.Equal(-2, result.EffectValue);
        Assert.True(result.CalculatedFromSource);
        Assert.True(result.SafetyPassed);
        Assert.Equal(64, result.EvidenceHash!.Length);
        Assert.Equal(result.EvidenceHash, repeated.EvidenceHash);
        Assert.True(result.LowerConfidenceBound < result.EffectValue);
        Assert.True(result.UpperConfidenceBound > result.EffectValue);
        Assert.True(result.StandardError > 0);
        Assert.True(result.DegreesOfFreedom > 0);
    }

    [Fact]
    public async Task Calculate_rejects_when_materialized_source_evidence_is_incomplete()
    {
        var trial = ConfirmatoryTrial();
        var source = new MemoryEvidenceSource(
            trial.ControlCycleIds.Take(4).Select((id, index) => Observation(id, 10 + index)).ToArray(),
            []);

        var error = await Assert.ThrowsAsync<ProcessImprovementRuleException>(() =>
            new ScientificTrialResultCalculator(source).CalculateAsync(trial, "scientist-a"));

        Assert.Contains("源数据样本量不足", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calculate_rejects_cross_version_feature_comparisons()
    {
        var trial = ConfirmatoryTrial();
        var primary = trial.ControlCycleIds.Select((id, index) => Observation(id, 10 + index))
            .Concat(trial.TrialCycleIds.Select((id, index) =>
                index == 0
                    ? Observation(id, 8 + index) with { DataModelVersion = 2 }
                    : Observation(id, 8 + index)))
            .ToArray();
        var source = new MemoryEvidenceSource(
            primary,
            trial.TrialCycleIds.Select(id => Observation(id, 180)).ToArray());

        var error = await Assert.ThrowsAsync<ProcessImprovementRuleException>(() =>
            new ScientificTrialResultCalculator(source).CalculateAsync(trial, "scientist-a"));

        Assert.Contains("不能直接比较", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_requires_every_confirmatory_safety_constraint_to_have_source_binding()
    {
        var trial = ConfirmatoryTrial() with
        {
            Protocol = ConfirmatoryTrial().Protocol! with { SafetyMetricBindings = [] }
        };

        var valid = ProcessImprovementValidator.TryValidate(trial, out _, out var error);

        Assert.False(valid);
        Assert.Contains("每个安全约束", error, StringComparison.Ordinal);
    }

    private static ProcessTrial ConfirmatoryTrial()
    {
        var control = Enumerable.Range(1, 10).Select(index => $"control-{index:00}").ToArray();
        var treatment = Enumerable.Range(1, 10).Select(index => $"trial-{index:00}").ToArray();
        return new ProcessTrial
        {
            TrialId = Guid.Parse("019c0000-0000-7000-8000-000000000001"),
            InvestigationId = Guid.Parse("019c0000-0000-7000-8000-000000000002"),
            CauseId = Guid.Parse("019c0000-0000-7000-8000-000000000003"),
            Name = "验证保温温度调整",
            RigorLevel = TrialRigorLevels.Confirmatory,
            Status = ProcessTrialStatuses.Running,
            StopRule = "安全指标越界立即停止。",
            RollbackPlan = "恢复基准配方。",
            CreatedBy = "scientist-a",
            ParameterChanges =
            [
                new TrialParameterChange
                {
                    ParameterCode = "temperature.setpoint",
                    BaselineValue = 180,
                    TrialValue = 176,
                    AllowedMinimum = 170,
                    AllowedMaximum = 185,
                    Unit = "°C"
                }
            ],
            SafetyConstraints =
            [
                new OperatingConstraint
                {
                    Code = "temperature-maximum",
                    Description = "最高温度",
                    Operator = "<=",
                    Limit = 185,
                    Unit = "°C"
                }
            ],
            ControlCycleIds = control,
            TrialCycleIds = treatment,
            Protocol = new ExperimentalProtocol
            {
                Hypothesis = "降低设定温度可降低主要温度特征且不突破安全上限。",
                PrimaryMetric = new TrialMetricDefinition
                {
                    MetricCode = "temperature-mean",
                    SignalCode = "temperature",
                    FeatureCode = "mean",
                    Unit = "°C",
                    Direction = "lower-is-better"
                },
                MinimumControlSampleSize = 10,
                MinimumTrialSampleSize = 10,
                Alpha = 0.05,
                SafetyMetricBindings =
                [
                    new TrialSafetyMetricBinding
                    {
                        ConstraintCode = "temperature-maximum",
                        SignalCode = "temperature",
                        FeatureCode = "max"
                    }
                ],
                PreRegisteredBy = "scientist-a",
                PreRegisteredAt = DateTimeOffset.Parse("2026-07-24T00:00:00Z")
            }
        };
    }

    private static TrialFeatureObservation Observation(string correlationId, double value)
        => new()
        {
            CorrelationId = correlationId,
            Value = value,
            Unit = "°C",
            FeatureDefinitionHash = new string('d', 64),
            ComputationHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes($"{correlationId}:{value}")))
                .ToLowerInvariant(),
            AlgorithmVersion = "stage-relative-v2",
            DataModelId = "heat-treatment",
            DataModelVersion = 1,
            AnalysisPlanId = "heat-treatment-cycle",
            AnalysisPlanVersion = 1
        };

    private sealed class MemoryEvidenceSource(
        IReadOnlyList<TrialFeatureObservation> primary,
        IReadOnlyList<TrialFeatureObservation> safety) : ITrialEvidenceSource
    {
        public Task<IReadOnlyList<TrialFeatureObservation>> ReadAsync(
            IReadOnlyList<string> cycleIds,
            string signalCode,
            string featureCode,
            string? phaseCode,
            int? phaseOrder,
            CancellationToken ct = default)
        {
            var source = featureCode == "max" ? safety : primary;
            var selected = source.Where(item => cycleIds.Contains(item.CorrelationId, StringComparer.Ordinal)).ToArray();
            return Task.FromResult<IReadOnlyList<TrialFeatureObservation>>(selected);
        }
    }
}
