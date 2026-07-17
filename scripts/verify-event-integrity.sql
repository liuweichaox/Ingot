\pset pager off
\if :{?edge_id}
\else
  \set edge_id ''
\endif

\echo '=== Event identity invariants (all counts must match) ==='
SELECT
  edge_id,
  COUNT(*) AS rows,
  COUNT(DISTINCT event_id) AS distinct_event_ids,
  COUNT(DISTINCT seq) AS distinct_sequences,
  MIN(seq) AS min_seq,
  MAX(seq) AS max_seq
FROM production_events
WHERE NULLIF(:'edge_id', '') IS NULL OR edge_id = :'edge_id'
GROUP BY edge_id
ORDER BY edge_id;

\echo '=== Sequence gaps (no rows means continuous delivery) ==='
WITH ordered AS (
  SELECT
    edge_id,
    seq,
    LAG(seq) OVER (PARTITION BY edge_id ORDER BY seq) AS previous_seq
  FROM production_events
  WHERE NULLIF(:'edge_id', '') IS NULL OR edge_id = :'edge_id'
)
SELECT
  edge_id,
  previous_seq + 1 AS missing_from,
  seq - 1 AS missing_to,
  seq - previous_seq - 1 AS missing_count
FROM ordered
WHERE previous_seq IS NOT NULL
  AND seq > previous_seq + 1
ORDER BY edge_id, missing_from;

\echo '=== Reserved ingest keys without facts (no rows expected) ==='
SELECT keys.edge_id, keys.seq, keys.event_id
FROM event_ingest_keys AS keys
LEFT JOIN production_events AS events
  ON events.event_id = keys.event_id
WHERE events.event_id IS NULL
  AND (NULLIF(:'edge_id', '') IS NULL OR keys.edge_id = :'edge_id')
ORDER BY keys.edge_id, keys.seq;

\echo '=== Hard integrity gate ==='
WITH stats AS (
  SELECT
    COUNT(*) AS rows,
    COUNT(DISTINCT event_id) AS distinct_event_ids,
    COUNT(DISTINCT seq) AS distinct_sequences,
    MIN(seq) AS min_seq,
    MAX(seq) AS max_seq
  FROM production_events
  WHERE NULLIF(:'edge_id', '') IS NULL OR edge_id = :'edge_id'
),
gaps AS (
  SELECT COUNT(*) AS gap_count
  FROM (
    SELECT
      edge_id,
      seq,
      LAG(seq) OVER (PARTITION BY edge_id ORDER BY seq) AS previous_seq
    FROM production_events
    WHERE NULLIF(:'edge_id', '') IS NULL OR edge_id = :'edge_id'
  ) AS ordered
  WHERE previous_seq IS NOT NULL
    AND seq <> previous_seq + 1
),
orphans AS (
  SELECT COUNT(*) AS orphan_count
  FROM event_ingest_keys AS keys
  LEFT JOIN production_events AS events
    ON events.event_id = keys.event_id
  WHERE events.event_id IS NULL
    AND (NULLIF(:'edge_id', '') IS NULL OR keys.edge_id = :'edge_id')
)
SELECT
  (
    stats.rows > 0
    AND stats.rows = stats.distinct_event_ids
    AND stats.rows = stats.distinct_sequences
    AND stats.min_seq = 1
    AND stats.max_seq = stats.rows
    AND gaps.gap_count = 0
    AND orphans.orphan_count = 0
  ) AS integrity_ok,
  stats.rows AS integrity_rows,
  stats.distinct_event_ids AS integrity_event_ids,
  stats.distinct_sequences AS integrity_sequences,
  stats.min_seq AS integrity_min_seq,
  stats.max_seq AS integrity_max_seq,
  gaps.gap_count AS integrity_gap_count,
  orphans.orphan_count AS integrity_orphan_count
FROM stats, gaps, orphans
\gset ingot_

\if :ingot_integrity_ok
  \echo 'Event integrity: PASS'
\else
  \echo 'Event integrity: FAIL'
  \echo 'rows=' :ingot_integrity_rows ' eventIds=' :ingot_integrity_event_ids ' sequences=' :ingot_integrity_sequences
  \echo 'range=' :ingot_integrity_min_seq '-' :ingot_integrity_max_seq ' gaps=' :ingot_integrity_gap_count ' orphanReservations=' :ingot_integrity_orphan_count
  \quit 1
\endif
