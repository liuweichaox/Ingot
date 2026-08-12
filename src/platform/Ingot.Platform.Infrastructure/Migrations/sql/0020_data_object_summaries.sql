-- Move the last user-schema objects out of PostgresPlatformEventStore startup DDL.
CREATE TABLE IF NOT EXISTS data_object_summaries (
  subject_type              TEXT NOT NULL,
  subject_id                TEXT NOT NULL,
  edge_id                   TEXT,
  event_count               BIGINT NOT NULL DEFAULT 0,
  sample_count              BIGINT NOT NULL DEFAULT 0,
  operation_count           BIGINT NOT NULL DEFAULT 0,
  first_observed_at         TIMESTAMPTZ,
  last_observed_at          TIMESTAMPTZ,
  last_sample_at            TIMESTAMPTZ,
  maximum_sample_gap_seconds DOUBLE PRECISION,
  latest_event_type         TEXT,
  context                   JSONB NOT NULL DEFAULT '{}'::jsonb,
  latest_ingest_id          BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (subject_type, subject_id)
);

CREATE TABLE IF NOT EXISTS data_object_operation_keys (
  subject_type   TEXT NOT NULL,
  subject_id     TEXT NOT NULL,
  correlation_id TEXT NOT NULL,
  PRIMARY KEY (subject_type, subject_id, correlation_id)
);

INSERT INTO data_object_operation_keys(subject_type, subject_id, correlation_id)
SELECT DISTINCT subject_type, subject_id, correlation_id
FROM production_events
WHERE correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

WITH aggregate_rows AS (
  SELECT subject_type, subject_id,
         count(*) AS event_count,
         count(*) FILTER (WHERE event_type = 'process.sample') AS sample_count,
         count(DISTINCT correlation_id) AS operation_count,
         min(occurred_at) AS first_observed_at,
         max(occurred_at) AS last_observed_at,
         max(occurred_at) FILTER (WHERE event_type = 'process.sample') AS last_sample_at
  FROM production_events
  GROUP BY subject_type, subject_id
),
latest_rows AS (
  SELECT DISTINCT ON (subject_type, subject_id)
         subject_type, subject_id, edge_id, event_type, context, ingest_id
  FROM production_events
  ORDER BY subject_type, subject_id, occurred_at DESC, ingest_id DESC
),
sample_intervals AS (
  SELECT subject_type, subject_id,
         EXTRACT(EPOCH FROM occurred_at - lag(occurred_at) OVER (
           PARTITION BY subject_type, subject_id ORDER BY occurred_at, ingest_id
         )) AS gap_seconds
  FROM production_events
  WHERE event_type = 'process.sample'
),
gap_rows AS (
  SELECT subject_type, subject_id, max(gap_seconds) AS maximum_sample_gap_seconds
  FROM sample_intervals
  GROUP BY subject_type, subject_id
)
INSERT INTO data_object_summaries(
  subject_type, subject_id, edge_id, event_count, sample_count, operation_count,
  first_observed_at, last_observed_at, last_sample_at, maximum_sample_gap_seconds,
  latest_event_type, context, latest_ingest_id)
SELECT aggregate_rows.subject_type, aggregate_rows.subject_id, latest_rows.edge_id,
       aggregate_rows.event_count, aggregate_rows.sample_count, aggregate_rows.operation_count,
       aggregate_rows.first_observed_at, aggregate_rows.last_observed_at,
       aggregate_rows.last_sample_at, gap_rows.maximum_sample_gap_seconds,
       latest_rows.event_type, latest_rows.context, latest_rows.ingest_id
FROM aggregate_rows
JOIN latest_rows USING (subject_type, subject_id)
LEFT JOIN gap_rows USING (subject_type, subject_id)
ON CONFLICT (subject_type, subject_id) DO UPDATE SET
  edge_id = EXCLUDED.edge_id,
  event_count = EXCLUDED.event_count,
  sample_count = EXCLUDED.sample_count,
  operation_count = EXCLUDED.operation_count,
  first_observed_at = EXCLUDED.first_observed_at,
  last_observed_at = EXCLUDED.last_observed_at,
  last_sample_at = EXCLUDED.last_sample_at,
  maximum_sample_gap_seconds = EXCLUDED.maximum_sample_gap_seconds,
  latest_event_type = EXCLUDED.latest_event_type,
  context = EXCLUDED.context,
  latest_ingest_id = EXCLUDED.latest_ingest_id;
