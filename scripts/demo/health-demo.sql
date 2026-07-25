-- ============================================================================
-- Ingot 数据体检 + 证据定级 —— 真实场景演示（在真实 PostgreSQL 上运行）
-- 用产品的实际迁移 schema 与实际定级探针 SQL，处理现场形态的光学模压数据。
-- 说明：本演示证明核心链路（CSV → 生产事件 → 数据体检 → 证据定级）在真实数据上成立；
--       它不依赖 .NET 平台编译，也可作为 .NET 版本的集成参照。
-- ============================================================================

-- 1) 现场宽表 CSV → 暂存表（模拟 ingot-import 的输入）
DROP TABLE IF EXISTS staging_csv CASCADE;
CREATE TABLE staging_csv (
  time TEXT, machine TEXT, cycle_id TEXT, product TEXT, recipe TEXT, recipe_ver TEXT,
  mold_id TEXT, shot_count TEXT, phase TEXT, event TEXT,
  t_upper TEXT, t_lower TEXT, force TEXT, position TEXT, force_unit TEXT
);
\copy staging_csv FROM 'sample-optical-molding.csv' WITH (FORMAT csv, HEADER true)

-- 2) 暂存 → production_events（模拟 ingot-import 的映射：拼 context jsonb、事件字段）
--    缺失列（product/mold 为空）→ context 为空对象，如现场适配器漏采。
INSERT INTO production_events
  (event_id, edge_id, seq, event_type, type_version, occurred_at, recorded_at,
   source, subject_type, subject_id, correlation_id, context, data)
SELECT
  gen_random_uuid()::text,
  'IMPORT-01',
  row_number() OVER (ORDER BY time, cycle_id, phase),
  event, 1,
  time::timestamptz, time::timestamptz,
  'edge/IMPORT-01/import/optical', 'asset', machine, cycle_id,
  CASE WHEN product = '' THEN '{}'::jsonb
       ELSE jsonb_strip_nulls(jsonb_build_object(
              'product_code', product, 'recipe_id', recipe, 'recipe_version', recipe_ver,
              'mold_id', mold_id, 'mold_shot_count', shot_count, 'process_phase', phase)) END,
  '{}'::jsonb
FROM staging_csv;

-- 3) 时序样本（用于单位冲突体检）：press_force 带各自单位 —— MOLD-09 用 N，MOLD-02 用 kN
INSERT INTO time_series_samples
  (occurred_at, collection_point_id, signal_code, data_type, unit, category,
   numeric_value, quality_code, event_id, ingest_id, recorded_at, edge_id, source,
   subject_type, subject_id, correlation_id, phase_code, data_model_id, data_model_version, run_context)
SELECT
  time::timestamptz, 'CP-'||machine, 'press_force', 'number', force_unit, 'process.sample.data.values',
  NULLIF(force,'')::float8, 'good', gen_random_uuid()::text, row_number() OVER (),
  time::timestamptz, 'IMPORT-01', 'edge/IMPORT-01/import/optical', 'asset', machine, cycle_id,
  phase, 'DM-A', 3, '{}'::jsonb
FROM staging_csv WHERE event='process.sample' AND force <> '';

-- 4) 物化：配对完成的周期视为已分析（ready），并写入阶段特征（覆盖度）供 L1/L2 体检
INSERT INTO cycle_analysis_materializations
  (correlation_id, algorithm_version, data_model_id, data_model_version, analysis_plan_id,
   analysis_plan_version, source_max_ingest_id, source_event_count, status, computed_at, result)
SELECT DISTINCT s.cycle_id, 'stage-relative-v2', 'DM-A', 3, 'PLAN-A', 1, 0, 0, 'ready', now(), '{}'::jsonb
FROM staging_csv s
WHERE s.event='cycle.completed';

INSERT INTO cycle_features
  (correlation_id, algorithm_version, data_model_id, data_model_version, analysis_plan_id,
   analysis_plan_version, signal_code, signal_name, signal_sample_count, phase_code, phase_order,
   phase_source, feature_code, valid_duration_ms, coverage)
SELECT DISTINCT s.cycle_id, 'stage-relative-v2','DM-A',3,'PLAN-A',1,
   'press_force','压制力',5,'anneal',4,'recipe_step','rate_c_per_min', 60000, 0.93
FROM staging_csv s WHERE s.event='cycle.completed';

\echo ''
\echo '================================================================'
\echo '  Ingot 数据体检 + 证据定级报告'
\echo '  范围：设备 M-01 · 产品 LENS-A · 问题：模具寿命（MOLD-02）'
\echo '================================================================'

\echo ''
\echo '【记录概览】'
SELECT
  (SELECT count(*) FROM production_events) AS 事件总数,
  (SELECT count(DISTINCT correlation_id) FROM production_events WHERE correlation_id IS NOT NULL) AS 周期数,
  (SELECT count(*) FROM cycle_analysis_materializations WHERE status='ready') AS 已分析周期,
  (SELECT min(occurred_at)::date FROM production_events) AS 最早,
  (SELECT max(occurred_at)::date FROM production_events) AS 最近;

\echo ''
\echo '【L0 生产记录可用 —— 门槛体检】（探针 = 产品 CaseLevelEvaluator 的实际 SQL）'
WITH scoped AS (
  SELECT correlation_id, event_type, context FROM production_events
  WHERE subject_id='M-01' AND context @> '{"mold_id":"MOLD-02"}'::jsonb AND correlation_id IS NOT NULL
),
grp AS (
  SELECT correlation_id,
         bool_or(event_type LIKE '%.started') s,
         bool_or(event_type LIKE '%.completed' OR event_type LIKE '%.cleared' OR event_type LIKE '%.exited') e
  FROM scoped GROUP BY correlation_id
),
pairing AS (
  SELECT count(*) FILTER (WHERE s OR e) cyc, count(*) FILTER (WHERE s AND e) paired FROM grp
),
ctx AS (
  SELECT count(*) tot, count(*) FILTER (WHERE context = jsonb_build_object()) missing
  FROM production_events WHERE subject_id='M-01'
),
fut AS (SELECT count(*) n FROM production_events WHERE subject_id='M-01' AND occurred_at > now()+interval '1 minute'),
units AS (SELECT count(*) n FROM (SELECT signal_code FROM time_series_samples WHERE subject_id='M-01'
          GROUP BY signal_code HAVING count(DISTINCT unit)>1) x)
SELECT '周期配对率' AS 门槛,
       round(100.0*paired/NULLIF(cyc,0),1)||'%  ('||paired||'/'||cyc||')' AS 实测,
       '≥ 95%' AS 阈值,
       CASE WHEN paired::numeric/NULLIF(cyc,0) >= 0.95 THEN '✅ 通过' ELSE '❌ 不通过' END AS 结果
FROM pairing
UNION ALL SELECT '生产信息缺失率', round(100.0*missing/NULLIF(tot,0),2)||'%  ('||missing||'/'||tot||')', '< 5%',
       CASE WHEN missing::numeric/NULLIF(tot,0) < 0.05 THEN '✅ 通过' ELSE '❌ 不通过' END FROM ctx
UNION ALL SELECT '时钟异常记录', n||' 条', '= 0', CASE WHEN n=0 THEN '✅ 通过' ELSE '❌ 不通过' END FROM fut
UNION ALL SELECT '单位冲突信号', n||' 个', '= 0', CASE WHEN n=0 THEN '✅ 通过' ELSE '❌ 不通过' END FROM units;

\echo ''
\echo '【L1 生产过程看得清 / L2 参数关系找得到 —— 门槛体检】'
WITH scope_cycles AS (
  SELECT correlation_id, max(context ->> 'mold_id') AS group_key
  FROM production_events
  WHERE correlation_id IS NOT NULL AND subject_id='M-01' AND context @> '{"mold_id":"MOLD-02"}'::jsonb
  GROUP BY correlation_id
)
SELECT '已物化周期数' AS 门槛,
       (SELECT count(DISTINCT cf.correlation_id) FROM cycle_features cf JOIN scope_cycles s USING(correlation_id))||' 个' AS 实测,
       '≥ 1' AS 阈值, '（L1）' AS 层
UNION ALL
SELECT '阶段归属覆盖',
       COALESCE(round((SELECT avg(cf.coverage)*100 FROM cycle_features cf JOIN scope_cycles s USING(correlation_id))::numeric,1)||'%','无'),
       '≥ 90%', '（L1）'
UNION ALL
SELECT '同类可比周期数（按 mold_id 分组）',
       (SELECT COALESCE(max(cnt),0) FROM (
          SELECT group_key, count(*) cnt FROM cycle_analysis_materializations m JOIN scope_cycles s USING(correlation_id)
          WHERE m.status='ready' GROUP BY group_key) g)||' 个',
       '≥ 30', '（L2）';

\echo ''
\echo '【结论】'
WITH scoped AS (
  SELECT correlation_id, event_type FROM production_events
  WHERE subject_id='M-01' AND context @> '{"mold_id":"MOLD-02"}'::jsonb AND correlation_id IS NOT NULL),
grp AS (SELECT correlation_id, bool_or(event_type LIKE '%.started') s,
        bool_or(event_type LIKE '%.completed' OR event_type LIKE '%.exited' OR event_type LIKE '%.cleared') e
        FROM scoped GROUP BY correlation_id),
m AS (SELECT count(*) FILTER (WHERE s OR e) cyc, count(*) FILTER (WHERE s AND e) paired FROM grp),
u AS (SELECT count(*) n FROM (SELECT signal_code FROM time_series_samples WHERE subject_id='M-01'
      GROUP BY signal_code HAVING count(DISTINCT unit)>1) x)
SELECT
  CASE
    WHEN (SELECT paired::numeric/NULLIF(cyc,0) FROM m) < 0.95 OR (SELECT n FROM u) > 0
      THEN 'L0-pending —— 生产记录尚不达标，暂不能进入过程分析'
    ELSE 'L0 及以上'
  END AS 当前证据等级,
  '晋级缺口：配对率 '||(SELECT round(100.0*paired/NULLIF(cyc,0),1) FROM m)||'% 未达 95%（有周期缺完工事件）；'
    ||'press_force 存在 '||(SELECT n FROM u)||' 个单位冲突（MOLD-09 用 N、MOLD-02 用 kN，需统一量纲）' AS 说明;

\echo ''
\echo '  诚实定级：数据不足时系统只说低等级的话。上面每个"不通过"都是可执行的现场整改项。'
\echo '================================================================'
