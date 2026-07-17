\pset pager off
\if :{?material_lot}
\else
  \set material_lot ''
\endif

\echo '=== Recent production cycles ==='
WITH cycle_events AS (
  SELECT
    edge_id,
    correlation_id,
    subject_type,
    subject_id,
    MIN(occurred_at) FILTER (WHERE event_type = 'cycle.started') AS started_at,
    MAX(occurred_at) FILTER (WHERE event_type = 'cycle.completed') AS completed_at,
    MAX(context ->> 'material_lot') AS material_lot,
    MAX(context ->> 'tooling') AS tooling,
    MAX(data ->> 'recipe_id') FILTER (WHERE event_type = 'cycle.started') AS recipe_id,
    MAX((data ->> 'good_count')::bigint)
      FILTER (WHERE event_type = 'cycle.completed' AND data ? 'good_count') AS good_count
  FROM production_events
  WHERE event_type IN ('cycle.started', 'cycle.completed')
    AND (
      NULLIF(:'material_lot', '') IS NULL
      OR context ->> 'material_lot' = :'material_lot'
    )
  GROUP BY edge_id, correlation_id, subject_type, subject_id
)
SELECT
  edge_id,
  subject_type || '/' || subject_id AS subject,
  material_lot,
  tooling,
  recipe_id,
  good_count,
  started_at,
  completed_at,
  ROUND(EXTRACT(EPOCH FROM (completed_at - started_at))::numeric, 3) AS duration_seconds
FROM cycle_events
WHERE started_at IS NOT NULL
ORDER BY started_at DESC
LIMIT 100;

\echo '=== Material trace ==='
SELECT
  occurred_at,
  edge_id,
  event_type,
  subject_type || '/' || subject_id AS subject,
  correlation_id,
  context,
  data
FROM production_events
WHERE NULLIF(:'material_lot', '') IS NOT NULL
  AND context ->> 'material_lot' = :'material_lot'
ORDER BY occurred_at, ingest_id;
