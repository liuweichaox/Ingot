using System.Text.Json;
using Ingot.Contracts.Insight;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Insight;

/// <summary>
///     对问题档案的绑定范围运行确定性 SQL 探针，评定证据等级。
///     L0 滚动门槛用 WindowDays 近窗（近期数据健康）；L1/L2 用档案完整窗口（同类周期随时间累积）。
///     探针 SQL 已在 PostgreSQL 16 + 合成数据上逐条验证。
/// </summary>
public sealed class CaseLevelEvaluator : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly CaseLevelThresholds _thresholds;

    public CaseLevelEvaluator(IConfiguration configuration, IOptions<CaseLevelThresholds> thresholds)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _thresholds = thresholds.Value;
    }

    public async Task<LevelEvaluation> EvaluateAsync(ProblemCase problemCase, CancellationToken ct = default)
    {
        var metrics = await ReadMetricsAsync(problemCase.Scope, ct).ConfigureAwait(false);
        var (level, gates) = CaseLevelGrading.Determine(metrics, _thresholds, problemCase.FeatureSetRatified);
        return new LevelEvaluation
        {
            CaseId = problemCase.CaseId,
            Level = level,
            Gates = gates,
            WindowDays = _thresholds.WindowDays,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
    }

    internal async Task<CaseLevelMetrics> ReadMetricsAsync(CaseScope scope, CancellationToken ct)
    {
        var filterJson = JsonSerializer.Serialize(scope.ContextFilter, JsonOptions);

        // ---- L0 行级度量：近窗内总数 / 空 context / 未来时间戳 ----
        long scopeEvents, missingContext, futureTimestamps;
        await using (var command = _dataSource.CreateCommand($"""
            SELECT
              count(*),
              count(*) FILTER (WHERE context = jsonb_build_object()),
              count(*) FILTER (WHERE occurred_at > now() + interval '1 minute')
            FROM production_events
            WHERE occurred_at >= now() - make_interval(days => @window)
              {SubjectPredicate(scope)}
              AND context @> @filter;
            """))
        {
            command.Parameters.AddWithValue("window", _thresholds.WindowDays);
            AddSubjectParam(command, scope);
            command.Parameters.Add(new NpgsqlParameter("filter", NpgsqlDbType.Jsonb) { Value = filterJson });
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            scopeEvents = reader.GetInt64(0);
            missingContext = reader.GetInt64(1);
            futureTimestamps = reader.GetInt64(2);
        }

        // ---- L0 配对率：近窗内每 correlation 是否同时有开始与结束事件 ----
        long cycles = 0, paired = 0;
        await using (var command = _dataSource.CreateCommand($"""
            WITH scoped AS (
              SELECT correlation_id, event_type FROM production_events
              WHERE occurred_at >= now() - make_interval(days => @window)
                {SubjectPredicate(scope)}
                AND context @> @filter
                AND correlation_id IS NOT NULL
            ),
            grp AS (
              SELECT correlation_id,
                     bool_or(event_type LIKE '%.started') AS s,
                     bool_or(event_type LIKE '%.completed'
                          OR event_type LIKE '%.cleared'
                          OR event_type LIKE '%.exited') AS e
              FROM scoped GROUP BY correlation_id
            )
            SELECT count(*) FILTER (WHERE s OR e), count(*) FILTER (WHERE s AND e) FROM grp;
            """))
        {
            command.Parameters.AddWithValue("window", _thresholds.WindowDays);
            AddSubjectParam(command, scope);
            command.Parameters.Add(new NpgsqlParameter("filter", NpgsqlDbType.Jsonb) { Value = filterJson });
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            cycles = reader.GetInt64(0);
            paired = reader.GetInt64(1);
        }

        // ---- L0 单位冲突：time_series_samples 中同一信号出现多种单位 ----
        long unitConflicts;
        await using (var command = _dataSource.CreateCommand($"""
            SELECT count(*) FROM (
              SELECT signal_code FROM time_series_samples
              WHERE TRUE {SubjectPredicate(scope)}
              GROUP BY signal_code HAVING count(DISTINCT unit) > 1
            ) x;
            """))
        {
            AddSubjectParam(command, scope);
            unitConflicts = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        // ---- L1/L2：档案完整窗口内的 scope 周期 → 物化/覆盖/可比数 ----
        long scopeCycles = 0, featuredCycles = 0, comparableReady = 0;
        double? avgCoverage = null;
        await using (var command = _dataSource.CreateCommand($"""
            WITH scope_cycles AS (
              SELECT correlation_id, max(context ->> @comparison_key) AS group_key
              FROM production_events
              WHERE correlation_id IS NOT NULL
                {SubjectPredicate(scope)}
                AND context @> @filter
                {WindowPredicate(scope)}
              GROUP BY correlation_id
            )
            SELECT
              (SELECT count(*) FROM scope_cycles),
              (SELECT count(DISTINCT cf.correlation_id)
                 FROM cycle_features cf JOIN scope_cycles s USING (correlation_id)),
              (SELECT avg(cf.coverage)
                 FROM cycle_features cf JOIN scope_cycles s USING (correlation_id)),
              -- 同类可比数 = 最大同 comparison_key 值分组内的 ready 周期数（键为空时退化为范围内 ready 总数）。
              -- 修正此前把不同 comparison_key 值的周期混算导致高估的缺陷。
              (SELECT COALESCE(max(cnt), 0) FROM (
                 SELECT group_key, count(*) AS cnt
                 FROM cycle_analysis_materializations m JOIN scope_cycles s USING (correlation_id)
                 WHERE m.status = 'ready'
                 GROUP BY group_key
               ) grouped);
            """))
        {
            AddSubjectParam(command, scope);
            command.Parameters.Add(new NpgsqlParameter("filter", NpgsqlDbType.Jsonb) { Value = filterJson });
            command.Parameters.AddWithValue("comparison_key", scope.ComparisonKey ?? string.Empty);
            AddWindowParams(command, scope);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            scopeCycles = reader.GetInt64(0);
            featuredCycles = reader.GetInt64(1);
            avgCoverage = reader.IsDBNull(2) ? null : reader.GetDouble(2);
            comparableReady = reader.GetInt64(3);
        }

        return new CaseLevelMetrics
        {
            ScopeEvents = scopeEvents,
            Cycles = cycles,
            PairedCycles = paired,
            MissingContext = missingContext,
            FutureTimestamps = futureTimestamps,
            UnitConflicts = unitConflicts,
            ScopeCycles = scopeCycles,
            FeaturedCycles = featuredCycles,
            AverageCoverage = avgCoverage,
            ComparableReadyCycles = comparableReady
        };
    }

    private static string SubjectPredicate(CaseScope scope)
        => string.IsNullOrWhiteSpace(scope.SubjectId) ? string.Empty : " AND subject_id = @subject_id";

    private static string WindowPredicate(CaseScope scope)
    {
        var lower = scope.WindowFrom.HasValue ? " AND occurred_at >= @from" : string.Empty;
        var upper = scope.WindowTo.HasValue ? " AND occurred_at <= @to" : string.Empty;
        return lower + upper;
    }

    private static void AddSubjectParam(NpgsqlCommand command, CaseScope scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.SubjectId))
            command.Parameters.AddWithValue("subject_id", scope.SubjectId.Trim());
    }

    private static void AddWindowParams(NpgsqlCommand command, CaseScope scope)
    {
        if (scope.WindowFrom.HasValue)
            command.Parameters.AddWithValue("from", scope.WindowFrom.Value.UtcDateTime);
        if (scope.WindowTo.HasValue)
            command.Parameters.AddWithValue("to", scope.WindowTo.Value.UtcDateTime);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
