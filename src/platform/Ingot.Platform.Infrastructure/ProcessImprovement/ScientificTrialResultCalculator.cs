using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ingot.Contracts.ProcessImprovement;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessImprovement;

public sealed record TrialFeatureObservation
{
    public required string CorrelationId { get; init; }
    public double Value { get; init; }
    public string? Unit { get; init; }
    public required string FeatureDefinitionHash { get; init; }
    public required string ComputationHash { get; init; }
    public required string AlgorithmVersion { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; }
    public required string AnalysisPlanId { get; init; }
    public int AnalysisPlanVersion { get; init; }
}

public interface ITrialEvidenceSource
{
    Task<IReadOnlyList<TrialFeatureObservation>> ReadAsync(
        IReadOnlyList<string> cycleIds,
        string signalCode,
        string featureCode,
        string? phaseCode,
        int? phaseOrder,
        CancellationToken ct = default);
}

public sealed class PostgresTrialEvidenceSource : ITrialEvidenceSource, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresTrialEvidenceSource(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<IReadOnlyList<TrialFeatureObservation>> ReadAsync(
        IReadOnlyList<string> cycleIds,
        string signalCode,
        string featureCode,
        string? phaseCode,
        int? phaseOrder,
        CancellationToken ct = default)
    {
        var normalizedIds = cycleIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedIds.Length == 0)
            return [];
        await using var command = _dataSource.CreateCommand(
            """
            WITH ranked AS (
              SELECT
                f.correlation_id,
                f.feature_value,
                f.signal_unit,
                f.feature_definition_hash,
                f.computation_hash,
                f.algorithm_version,
                f.data_model_id,
                f.data_model_version,
                f.analysis_plan_id,
                f.analysis_plan_version,
                ROW_NUMBER() OVER (
                  PARTITION BY f.correlation_id
                  ORDER BY m.computed_at DESC, m.algorithm_version DESC
                ) AS position
              FROM cycle_features f
              JOIN cycle_analysis_materializations m
                ON m.correlation_id = f.correlation_id
               AND m.algorithm_version = f.algorithm_version
               AND m.data_model_id = f.data_model_id
               AND m.data_model_version = f.data_model_version
               AND m.analysis_plan_id = f.analysis_plan_id
               AND m.analysis_plan_version = f.analysis_plan_version
              WHERE m.status = 'ready'
                AND f.correlation_id = ANY(@cycle_ids)
                AND f.signal_code = @signal_code
                AND f.feature_code = @feature_code
                AND f.phase_code = @phase_code
                AND f.phase_order = @phase_order
                AND f.feature_value IS NOT NULL
                AND f.feature_definition_hash <> ''
                AND f.computation_hash <> ''
            )
            SELECT correlation_id, feature_value, signal_unit, feature_definition_hash,
                   computation_hash, algorithm_version, data_model_id, data_model_version,
                   analysis_plan_id, analysis_plan_version
            FROM ranked
            WHERE position = 1
            ORDER BY correlation_id;
            """);
        command.Parameters.AddWithValue(
            "cycle_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            normalizedIds);
        command.Parameters.AddWithValue("signal_code", signalCode.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("feature_code", featureCode.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("phase_code", phaseCode?.Trim().ToLowerInvariant() ?? "");
        command.Parameters.AddWithValue("phase_order", phaseOrder ?? 0);
        var result = new List<TrialFeatureObservation>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new TrialFeatureObservation
            {
                CorrelationId = reader.GetString(0),
                Value = reader.GetDouble(1),
                Unit = reader.IsDBNull(2) ? null : reader.GetString(2),
                FeatureDefinitionHash = reader.GetString(3),
                ComputationHash = reader.GetString(4),
                AlgorithmVersion = reader.GetString(5),
                DataModelId = reader.GetString(6),
                DataModelVersion = reader.GetInt32(7),
                AnalysisPlanId = reader.GetString(8),
                AnalysisPlanVersion = reader.GetInt32(9)
            });
        }
        return result;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>
/// Computes a confirmatory trial result exclusively from versioned cycle features.
/// The result carries a deterministic evidence hash over the protocol and every input.
/// </summary>
public sealed class ScientificTrialResultCalculator(ITrialEvidenceSource evidenceSource)
{
    public async Task<TrialResult> CalculateAsync(
        ProcessTrial trial,
        string userId,
        CancellationToken ct = default)
    {
        if (trial.RigorLevel != TrialRigorLevels.Confirmatory || trial.Protocol is null)
            throw new ProcessImprovementRuleException("只有带预注册协议的验证性试验可以自动计算结果。");
        var protocol = trial.Protocol;
        var control = await evidenceSource.ReadAsync(
            trial.ControlCycleIds,
            protocol.PrimaryMetric.SignalCode,
            protocol.PrimaryMetric.FeatureCode,
            protocol.PrimaryMetric.PhaseCode,
            protocol.PrimaryMetric.PhaseOrder,
            ct).ConfigureAwait(false);
        var treatment = await evidenceSource.ReadAsync(
            trial.TrialCycleIds,
            protocol.PrimaryMetric.SignalCode,
            protocol.PrimaryMetric.FeatureCode,
            protocol.PrimaryMetric.PhaseCode,
            protocol.PrimaryMetric.PhaseOrder,
            ct).ConfigureAwait(false);
        if (control.Count < protocol.MinimumControlSampleSize ||
            treatment.Count < protocol.MinimumTrialSampleSize)
        {
            throw new ProcessImprovementRuleException(
                $"源数据样本量不足：基准 {control.Count}/{protocol.MinimumControlSampleSize}，"
                + $"试验 {treatment.Count}/{protocol.MinimumTrialSampleSize}。");
        }
        EnsureComparable(control.Concat(treatment).ToArray(), protocol.PrimaryMetric.Unit);

        var baseline = Mean(control);
        var trialValue = Mean(treatment);
        var baselineVariance = SampleVariance(control, baseline);
        var trialVariance = SampleVariance(treatment, trialValue);
        var firstTerm = baselineVariance / control.Count;
        var secondTerm = trialVariance / treatment.Count;
        var standardError = Math.Sqrt(firstTerm + secondTerm);
        var degreesOfFreedom = WelchDegreesOfFreedom(
            firstTerm,
            secondTerm,
            control.Count,
            treatment.Count);
        var critical = StudentTCritical(1 - protocol.Alpha / 2, degreesOfFreedom);
        var effect = trialValue - baseline;
        var safetyPassed = await EvaluateSafetyAsync(trial, ct).ConfigureAwait(false);
        var evidenceHash = EvidenceHash(trial, control, treatment);
        return new TrialResult
        {
            TrialId = trial.TrialId,
            MetricCode = protocol.PrimaryMetric.MetricCode,
            BaselineValue = baseline,
            TrialValue = trialValue,
            EffectValue = effect,
            Unit = protocol.PrimaryMetric.Unit,
            LowerConfidenceBound = effect - critical * standardError,
            UpperConfidenceBound = effect + critical * standardError,
            BaselineSampleCount = control.Count,
            TrialSampleCount = treatment.Count,
            SafetyPassed = safetyPassed,
            CalculatedFromSource = true,
            ComputationMethod = protocol.Estimator,
            EvidenceHash = evidenceHash,
            StandardError = standardError,
            DegreesOfFreedom = degreesOfFreedom,
            RecordedBy = userId,
            RecordedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<bool> EvaluateSafetyAsync(
        ProcessTrial trial,
        CancellationToken ct)
    {
        var protocol = trial.Protocol!;
        foreach (var constraint in trial.SafetyConstraints)
        {
            var binding = protocol.SafetyMetricBindings.Single(item =>
                string.Equals(item.ConstraintCode, constraint.Code, StringComparison.OrdinalIgnoreCase));
            var observations = await evidenceSource.ReadAsync(
                trial.TrialCycleIds,
                binding.SignalCode,
                binding.FeatureCode,
                binding.PhaseCode,
                binding.PhaseOrder,
                ct).ConfigureAwait(false);
            if (observations.Count != trial.TrialCycleIds.Distinct(StringComparer.Ordinal).Count() ||
                observations.Any(observation =>
                    !string.Equals(observation.Unit, constraint.Unit, StringComparison.Ordinal)) ||
                observations.Any(observation => !Satisfies(observation.Value, constraint)))
            {
                return false;
            }
        }
        return true;
    }

    private static void EnsureComparable(
        IReadOnlyList<TrialFeatureObservation> observations,
        string expectedUnit)
    {
        if (observations.Any(observation =>
                !string.Equals(observation.Unit, expectedUnit, StringComparison.Ordinal)))
        {
            throw new ProcessImprovementRuleException("源数据单位与预注册主要指标单位不一致。");
        }
        var calculationIdentities = observations.Select(observation => string.Join(
                "|",
                observation.FeatureDefinitionHash,
                observation.AlgorithmVersion,
                observation.DataModelId,
                observation.DataModelVersion,
                observation.AnalysisPlanId,
                observation.AnalysisPlanVersion))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (calculationIdentities.Length != 1)
        {
            throw new ProcessImprovementRuleException(
                "基准组与试验组使用了不同的特征定义、算法、数据模型或分析方案，不能直接比较。");
        }
    }

    private static bool Satisfies(double value, OperatingConstraint constraint)
        => constraint.Operator switch
        {
            "<" => value < constraint.Limit,
            "<=" => value <= constraint.Limit,
            ">" => value > constraint.Limit,
            ">=" => value >= constraint.Limit,
            "==" => Math.Abs(value - constraint.Limit) <= 1e-12,
            _ => false
        };

    private static double Mean(IReadOnlyList<TrialFeatureObservation> values)
        => values.Average(static value => value.Value);

    private static double SampleVariance(
        IReadOnlyList<TrialFeatureObservation> values,
        double mean)
        => values.Count < 2
            ? 0
            : values.Sum(value => Math.Pow(value.Value - mean, 2)) / (values.Count - 1);

    private static double WelchDegreesOfFreedom(
        double firstTerm,
        double secondTerm,
        int firstCount,
        int secondCount)
    {
        var denominator =
            Math.Pow(firstTerm, 2) / (firstCount - 1) +
            Math.Pow(secondTerm, 2) / (secondCount - 1);
        return denominator <= 0
            ? firstCount + secondCount - 2
            : Math.Pow(firstTerm + secondTerm, 2) / denominator;
    }

    private static double StudentTCritical(double probability, double degreesOfFreedom)
    {
        var z = InverseNormal(probability);
        var z2 = z * z;
        var first = (z * z2 + z) / (4 * degreesOfFreedom);
        var second = (5 * z * Math.Pow(z2, 2) + 16 * z * z2 + 3 * z) /
                     (96 * Math.Pow(degreesOfFreedom, 2));
        return z + first + second;
    }

    // Peter J. Acklam's rational approximation, sufficient for protocol alpha in [0.001, 0.2].
    private static double InverseNormal(double probability)
    {
        double[] a =
        [
            -3.969683028665376e+01, 2.209460984245205e+02,
            -2.759285104469687e+02, 1.383577518672690e+02,
            -3.066479806614716e+01, 2.506628277459239e+00
        ];
        double[] b =
        [
            -5.447609879822406e+01, 1.615858368580409e+02,
            -1.556989798598866e+02, 6.680131188771972e+01,
            -1.328068155288572e+01
        ];
        double[] c =
        [
            -7.784894002430293e-03, -3.223964580411365e-01,
            -2.400758277161838e+00, -2.549732539343734e+00,
            4.374664141464968e+00, 2.938163982698783e+00
        ];
        double[] d =
        [
            7.784695709041462e-03, 3.224671290700398e-01,
            2.445134137142996e+00, 3.754408661907416e+00
        ];
        const double lower = 0.02425;
        const double upper = 1 - lower;
        if (probability < lower)
        {
            var q = Math.Sqrt(-2 * Math.Log(probability));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (probability <= upper)
        {
            var q = probability - 0.5;
            var r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                   (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        var tail = Math.Sqrt(-2 * Math.Log(1 - probability));
        return -(((((c[0] * tail + c[1]) * tail + c[2]) * tail + c[3]) * tail + c[4]) * tail + c[5]) /
               ((((d[0] * tail + d[1]) * tail + d[2]) * tail + d[3]) * tail + 1);
    }

    private static string EvidenceHash(
        ProcessTrial trial,
        IReadOnlyList<TrialFeatureObservation> control,
        IReadOnlyList<TrialFeatureObservation> treatment)
    {
        var canonical = new StringBuilder()
            .Append(trial.TrialId).Append('|')
            .Append(trial.Protocol!.Estimator).Append('|')
            .Append(trial.Protocol.PrimaryMetric.SignalCode).Append('|')
            .Append(trial.Protocol.PrimaryMetric.FeatureCode).Append('|')
            .Append(trial.Protocol.PrimaryMetric.PhaseCode).Append('|')
            .Append(trial.Protocol.PrimaryMetric.PhaseOrder).Append('|')
            .Append(trial.Protocol.Alpha.ToString("R", CultureInfo.InvariantCulture));
        Append("control", control);
        Append("trial", treatment);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        void Append(string group, IReadOnlyList<TrialFeatureObservation> values)
        {
            foreach (var value in values.OrderBy(static item => item.CorrelationId, StringComparer.Ordinal))
            {
                canonical.Append('|').Append(group).Append(':')
                    .Append(value.CorrelationId).Append(':')
                    .Append(value.Value.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(value.FeatureDefinitionHash).Append(':')
                    .Append(value.ComputationHash);
            }
        }
    }
}
