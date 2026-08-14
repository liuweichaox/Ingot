using Ingot.Contracts.Events;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
/// Pushes phase-window and numerical feature work to TimescaleDB. The independent
/// deterministic engine remains the scientific reference: database output is accepted
/// only when it agrees with the reference result within a declared tolerance.
/// Both late-event recomputation and historical backfill enter through this service.
/// </summary>
public sealed class PostgresProcessExecutionScientificComputeEngine : IAsyncDisposable
{
    private const double RelativeTolerance = 1e-9;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresProcessExecutionScientificComputeEngine>? _logger;

    public PostgresProcessExecutionScientificComputeEngine(
        IConfiguration configuration,
        ILogger<PostgresProcessExecutionScientificComputeEngine>? logger = null)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
    }

    public async Task<WholeProcessExecutionAnalysisResult> ComputeAndVerifyAsync(
        string executionId,
        DateTimeOffset executionStartedAt,
        DateTimeOffset executionCompletedAt,
        WholeProcessExecutionAnalysisResult reference,
        CancellationToken ct = default)
    {
        if (!await HasProjectedSamplesAsync(executionId, ct).ConfigureAwait(false))
        {
            _logger?.LogInformation(
                "过程执行 {ExecutionId} 尚无时序投影，使用独立批处理基准完成分析物化",
                executionId);
            return reference;
        }

        var phases = await LoadPhasesAsync(
            executionId,
            executionCompletedAt,
            reference.Phases,
            ct).ConfigureAwait(false);
        var threshold = reference.Quality.MedianIntervalMs is > 0
            ? reference.Quality.MedianIntervalMs.Value * 5
            : double.MaxValue;
        var signals = new List<ProcessSignalStatistic>(reference.Signals.Count);
        foreach (var signal in reference.Signals)
        {
            var refined = new List<ProcessSignalFeature>(signal.Features.Count);
            foreach (var scope in signal.Features.GroupBy(
                         static feature => (feature.PhaseOrder ?? 0, feature.PhaseCode ?? ""),
                         ScopeComparer.Instance))
            {
                var first = scope.First();
                var startedAt = first.PhaseOrder.HasValue
                    ? phases.Single(phase => phase.Order == first.PhaseOrder.Value).StartedAt
                    : executionStartedAt;
                var endedAt = first.PhaseOrder.HasValue
                    ? phases.Single(phase => phase.Order == first.PhaseOrder.Value).EndedAt
                    : executionCompletedAt;
                if (!startedAt.HasValue || !endedAt.HasValue || endedAt <= startedAt)
                {
                    refined.AddRange(scope);
                    continue;
                }
                var computed = await ComputeScopeAsync(
                    executionId,
                    signal.Code,
                    startedAt.Value,
                    endedAt.Value,
                    threshold,
                    ct).ConfigureAwait(false);
                foreach (var feature in scope)
                {
                    var value = computed.ValueFor(feature.Code);
                    EnsureEquivalent(signal.Code, feature, value, computed);
                    refined.Add(feature with
                    {
                        Value = value,
                        InputPointCount = computed.InputPointCount,
                        ValidDurationMs = computed.ValidDurationMs,
                        Coverage = computed.Coverage
                    });
                }
            }
            var whole = refined.Where(static feature => feature.PhaseCode is null).ToArray();
            signals.Add(signal with
            {
                Average = FeatureValue(whole, "mean", "average"),
                Minimum = FeatureValue(whole, "min", "minimum"),
                Maximum = FeatureValue(whole, "max", "maximum"),
                ValidDurationMs = whole.FirstOrDefault()?.ValidDurationMs ?? 0,
                Coverage = whole.FirstOrDefault()?.Coverage ?? 0,
                Features = refined
            });
        }
        return reference with { Signals = signals, Phases = phases };
    }

    private async Task<bool> HasProjectedSamplesAsync(
        string executionId,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM time_series_samples WHERE execution_id = @execution_id LIMIT 1);");
        command.Parameters.AddWithValue("execution_id", executionId);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    private async Task<IReadOnlyList<ProcessPhaseSummary>> LoadPhasesAsync(
        string executionId,
        DateTimeOffset completedAt,
        IReadOnlyList<ProcessPhaseSummary> reference,
        CancellationToken ct)
    {
        if (reference.Count == 0)
            return reference;
        await using var command = _dataSource.CreateCommand(
            """
            WITH event_phases AS (
              SELECT occurred_at, MIN(phase_code) AS phase_code
              FROM time_series_samples
              WHERE execution_id = @execution_id
              GROUP BY occurred_at
            ),
            marked AS (
              SELECT
                occurred_at,
                COALESCE(phase_code, 'unknown') AS phase_code,
                CASE WHEN LAG(COALESCE(phase_code, 'unknown')) OVER (ORDER BY occurred_at)
                               IS DISTINCT FROM COALESCE(phase_code, 'unknown')
                     THEN 1 ELSE 0 END AS starts_new
              FROM event_phases
            ),
            numbered AS (
              SELECT *,
                     SUM(starts_new) OVER (ORDER BY occurred_at ROWS UNBOUNDED PRECEDING) AS phase_order
              FROM marked
            ),
            grouped AS (
              SELECT
                phase_order::INTEGER AS phase_order,
                phase_code,
                MIN(occurred_at) AS started_at,
                COUNT(*)::INTEGER AS sample_count
              FROM numbered
              GROUP BY phase_order, phase_code
            )
            SELECT
              phase_order,
              phase_code,
              started_at,
              LEAD(started_at, 1, @completed_at) OVER (ORDER BY phase_order) AS ended_at,
              sample_count
            FROM grouped
            ORDER BY phase_order;
            """);
        command.Parameters.AddWithValue("execution_id", executionId);
        command.Parameters.AddWithValue("completed_at", completedAt.UtcDateTime);
        var database = new List<(int Order, string Code, DateTimeOffset Start, DateTimeOffset End, int Count)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            database.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2)),
                new DateTimeOffset(reader.GetDateTime(3)),
                reader.GetInt32(4)));
        }
        if (database.Count != reference.Count)
            throw new ScientificComputeMismatchException("数据库阶段数量与批处理基准不一致。");
        var result = new List<ProcessPhaseSummary>(database.Count);
        foreach (var item in database)
        {
            var expected = reference.SingleOrDefault(phase => phase.Order == item.Order)
                ?? throw new ScientificComputeMismatchException($"数据库返回未知阶段序号 {item.Order}。");
            if (!string.Equals(expected.Code, item.Code, StringComparison.Ordinal) ||
                expected.StartedAt != item.Start ||
                expected.EndedAt != item.End ||
                expected.SampleCount != item.Count)
            {
                throw new ScientificComputeMismatchException(
                    $"阶段 {item.Order} 的数据库窗口与批处理基准不一致。");
            }
            result.Add(expected with
            {
                StartedAt = item.Start,
                EndedAt = item.End,
                SampleCount = item.Count
            });
        }
        return result;
    }

    private async Task<SqlScopeResult> ComputeScopeAsync(
        string executionId,
        string signalCode,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double interruptionThresholdMs,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            WITH points AS (
              SELECT
                occurred_at,
                ingest_id,
                COALESCE(numeric_value, integer_value::DOUBLE PRECISION) AS value
              FROM time_series_samples
              WHERE execution_id = @execution_id
                AND signal_code = @signal_code
                AND occurred_at >= @started_at
                AND occurred_at <= @ended_at
                AND (numeric_value IS NOT NULL OR integer_value IS NOT NULL)
            ),
            deduplicated AS (
              SELECT DISTINCT ON (occurred_at) occurred_at, value
              FROM points
              ORDER BY occurred_at, ingest_id DESC
            ),
            strict_points AS (
              SELECT * FROM deduplicated WHERE occurred_at < @ended_at
            ),
            ordered AS (
              SELECT
                occurred_at,
                value,
                LEAD(occurred_at) OVER (ORDER BY occurred_at) AS next_at,
                LEAD(value) OVER (ORDER BY occurred_at) AS next_value
              FROM deduplicated
            ),
            segments AS (
              SELECT
                occurred_at,
                value,
                next_at,
                next_value,
                EXTRACT(EPOCH FROM (next_at - occurred_at)) * 1000.0 AS duration_ms
              FROM ordered
              WHERE next_at IS NOT NULL
                AND next_at <= @ended_at
                AND EXTRACT(EPOCH FROM (next_at - occurred_at)) * 1000.0 > 0
                AND EXTRACT(EPOCH FROM (next_at - occurred_at)) * 1000.0 <= @threshold_ms
            ),
            segment_totals AS (
              SELECT
                COALESCE(SUM(duration_ms), 0.0) AS valid_duration_ms,
                SUM((value + next_value) * 0.5 * duration_ms) AS area_ms,
                SUM((value + next_value) * 0.5 * duration_ms / 1000.0) AS integral
              FROM segments
            ),
            centered_variance AS (
              SELECT SUM((
                  POWER(value - segment_totals.area_ms / segment_totals.valid_duration_ms, 2) +
                  (value - segment_totals.area_ms / segment_totals.valid_duration_ms) *
                    (next_value - segment_totals.area_ms / segment_totals.valid_duration_ms) +
                  POWER(next_value - segment_totals.area_ms / segment_totals.valid_duration_ms, 2)
                ) / 3.0 * duration_ms) AS centered_square_area_ms
              FROM segments CROSS JOIN segment_totals
              WHERE segment_totals.valid_duration_ms > 0
            ),
            endpoint_weights AS (
              SELECT occurred_at, value, duration_ms / 2.0 AS weight FROM segments
              UNION ALL
              SELECT next_at, next_value, duration_ms / 2.0 AS weight FROM segments
            ),
            weights AS (
              SELECT occurred_at, value, SUM(weight) AS weight
              FROM endpoint_weights
              GROUP BY occurred_at, value
            ),
            weighted_order AS (
              SELECT
                value,
                weight,
                SUM(weight) OVER (ORDER BY value, occurred_at) AS cumulative_weight,
                SUM(weight) OVER () AS total_weight
              FROM weights
            ),
            weighted_moments AS (
              SELECT
                SUM(weight) AS sum_w,
                SUM(weight * EXTRACT(EPOCH FROM (occurred_at - @started_at))) AS sum_wx,
                SUM(weight * value) AS sum_wy,
                SUM(weight * EXTRACT(EPOCH FROM (occurred_at - @started_at))
                           * EXTRACT(EPOCH FROM (occurred_at - @started_at))) AS sum_wxx,
                SUM(weight * EXTRACT(EPOCH FROM (occurred_at - @started_at)) * value) AS sum_wxy
              FROM weights
            ),
            scalar AS (
              SELECT
                (SELECT COUNT(*)::INTEGER FROM deduplicated) AS input_point_count,
                (SELECT MIN(value) FROM strict_points) AS minimum,
                (SELECT MAX(value) FROM strict_points) AS maximum,
                segment_totals.valid_duration_ms,
                segment_totals.area_ms,
                segment_totals.integral,
                centered_variance.centered_square_area_ms,
                COALESCE(
                  (SELECT MIN(value) FROM weighted_order
                    WHERE cumulative_weight >= total_weight * 0.05),
                  (SELECT percentile_cont(0.05) WITHIN GROUP (ORDER BY value) FROM deduplicated)
                ) AS p05,
                COALESCE(
                  (SELECT MIN(value) FROM weighted_order
                    WHERE cumulative_weight >= total_weight * 0.50),
                  (SELECT percentile_cont(0.50) WITHIN GROUP (ORDER BY value) FROM deduplicated)
                ) AS median,
                COALESCE(
                  (SELECT MIN(value) FROM weighted_order
                    WHERE cumulative_weight >= total_weight * 0.95),
                  (SELECT percentile_cont(0.95) WITHIN GROUP (ORDER BY value) FROM deduplicated)
                ) AS p95,
                weighted_moments.sum_w,
                weighted_moments.sum_wx,
                weighted_moments.sum_wy,
                weighted_moments.sum_wxx,
                weighted_moments.sum_wxy
              FROM segment_totals CROSS JOIN centered_variance CROSS JOIN weighted_moments
            )
            SELECT
              input_point_count,
              minimum,
              maximum,
              valid_duration_ms,
              CASE WHEN valid_duration_ms > 0 THEN area_ms / valid_duration_ms END AS mean,
              CASE WHEN valid_duration_ms > 0 THEN
                SQRT(GREATEST(0.0, centered_square_area_ms / valid_duration_ms))
              END AS stddev,
              p05,
              median,
              p95,
              integral,
              CASE WHEN sum_w * sum_wxx - sum_wx * sum_wx <> 0 THEN
                (sum_w * sum_wxy - sum_wx * sum_wy) /
                (sum_w * sum_wxx - sum_wx * sum_wx)
              END AS slope
            FROM scalar;
            """);
        command.Parameters.AddWithValue("execution_id", executionId);
        command.Parameters.AddWithValue("signal_code", signalCode);
        command.Parameters.AddWithValue("started_at", startedAt.UtcDateTime);
        command.Parameters.AddWithValue("ended_at", endedAt.UtcDateTime);
        command.Parameters.AddWithValue("threshold_ms", interruptionThresholdMs);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new SqlScopeResult();
        var duration = reader.GetDouble(3);
        return new SqlScopeResult
        {
            InputPointCount = reader.GetInt32(0),
            Minimum = NullableDouble(reader, 1),
            Maximum = NullableDouble(reader, 2),
            ValidDurationMs = duration,
            Coverage = Math.Clamp(duration / (endedAt - startedAt).TotalMilliseconds, 0, 1),
            Mean = NullableDouble(reader, 4),
            StandardDeviation = NullableDouble(reader, 5),
            P05 = NullableDouble(reader, 6),
            Median = NullableDouble(reader, 7),
            P95 = NullableDouble(reader, 8),
            Integral = NullableDouble(reader, 9),
            Slope = NullableDouble(reader, 10)
        };
    }

    private static double? NullableDouble(NpgsqlDataReader reader, int index)
        => reader.IsDBNull(index) ? null : reader.GetDouble(index);

    internal static void EnsureEquivalent(
        string signalCode,
        ProcessSignalFeature reference,
        double? databaseValue,
        SqlScopeResult database)
    {
        if (!Equivalent(reference.Value, databaseValue) ||
            !Equivalent(reference.ValidDurationMs, database.ValidDurationMs) ||
            !Equivalent(reference.Coverage, database.Coverage) ||
            reference.InputPointCount != database.InputPointCount)
        {
            throw new ScientificComputeMismatchException(
                $"信号 {signalCode} 特征 {reference.Code} 的数据库结果与批处理基准不一致：" +
                $"value={Format(reference.Value)}/{Format(databaseValue)}, " +
                $"duration={Format(reference.ValidDurationMs)}/{Format(database.ValidDurationMs)}, " +
                $"coverage={Format(reference.Coverage)}/{Format(database.Coverage)}, " +
                $"points={reference.InputPointCount}/{database.InputPointCount}。");
        }
    }

    private static string Format(double? value)
        => value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null";

    private static bool Equivalent(double? expected, double? actual)
        => expected is null && actual is null ||
           expected.HasValue && actual.HasValue &&
           Math.Abs(expected.Value - actual.Value) <=
           RelativeTolerance * (1 + Math.Max(Math.Abs(expected.Value), Math.Abs(actual.Value)));

    private static double? FeatureValue(
        IReadOnlyList<ProcessSignalFeature> features,
        params string[] codes)
        => features.FirstOrDefault(feature => codes.Contains(feature.Code, StringComparer.Ordinal))?.Value;

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    internal sealed record SqlScopeResult
    {
        public int InputPointCount { get; init; }
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
        public double ValidDurationMs { get; init; }
        public double Coverage { get; init; }
        public double? Mean { get; init; }
        public double? StandardDeviation { get; init; }
        public double? P05 { get; init; }
        public double? Median { get; init; }
        public double? P95 { get; init; }
        public double? Integral { get; init; }
        public double? Slope { get; init; }

        public double? ValueFor(string code)
            => code switch
            {
                "mean" or "average" => Mean,
                "min" or "minimum" => Minimum,
                "max" or "maximum" => Maximum,
                "range" => Minimum.HasValue && Maximum.HasValue ? Maximum - Minimum : null,
                "std" or "stddev" => StandardDeviation,
                "p05" => P05,
                "median" => Median,
                "p95" => P95,
                "integral" => Integral,
                "slope" => Slope,
                _ => null
            };
    }

    private sealed class ScopeComparer : IEqualityComparer<(int Order, string Code)>
    {
        public static ScopeComparer Instance { get; } = new();

        public bool Equals((int Order, string Code) x, (int Order, string Code) y)
            => x.Order == y.Order && string.Equals(x.Code, y.Code, StringComparison.Ordinal);

        public int GetHashCode((int Order, string Code) obj)
            => HashCode.Combine(obj.Order, StringComparer.Ordinal.GetHashCode(obj.Code));
    }
}

public sealed class ScientificComputeMismatchException(string message) : Exception(message);
