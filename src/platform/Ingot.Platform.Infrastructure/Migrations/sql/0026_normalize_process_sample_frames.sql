-- Normalize process samples into one frame row plus N typed value rows.
-- No legacy table, compatibility view, or fallback read path remains.

ALTER TABLE collection_points
  ADD COLUMN point_key BIGINT GENERATED ALWAYS AS IDENTITY;
CREATE UNIQUE INDEX ux_collection_points_key
  ON collection_points (point_key);

CREATE TABLE process_sample_frames (
  occurred_at        TIMESTAMPTZ NOT NULL,
  frame_id           BIGINT NOT NULL,
  event_id           TEXT NOT NULL,
  recorded_at        TIMESTAMPTZ NOT NULL,
  ingested_at        TIMESTAMPTZ NOT NULL,
  edge_id            TEXT NOT NULL,
  source             TEXT NOT NULL,
  subject_type       TEXT NOT NULL,
  subject_id         TEXT NOT NULL,
  execution_id       TEXT,
  phase_code         TEXT,
  data_model_id      TEXT NOT NULL,
  data_model_version INTEGER NOT NULL
);

CREATE UNIQUE INDEX ux_process_sample_frames_event
  ON process_sample_frames (event_id, occurred_at);
CREATE UNIQUE INDEX ux_process_sample_frames_id
  ON process_sample_frames (frame_id, occurred_at);
CREATE INDEX ix_process_sample_frames_execution
  ON process_sample_frames (execution_id, occurred_at);
CREATE INDEX ix_process_sample_frames_subject
  ON process_sample_frames (subject_type, subject_id, occurred_at);

CREATE TABLE process_sample_values (
  occurred_at        TIMESTAMPTZ NOT NULL,
  frame_id           BIGINT NOT NULL,
  point_key          BIGINT NOT NULL,
  quality_code       SMALLINT NOT NULL,
  numeric_value      DOUBLE PRECISION,
  integer_value      BIGINT,
  boolean_value      BOOLEAN,
  text_value         TEXT,
  CONSTRAINT ck_process_sample_values_one_value CHECK (
    num_nonnulls(numeric_value, integer_value, boolean_value, text_value) = 1
  ),
  CONSTRAINT ck_process_sample_values_quality CHECK (
    quality_code BETWEEN 0 AND 2
  )
);

CREATE UNIQUE INDEX ux_process_sample_values_point
  ON process_sample_values (frame_id, point_key, occurred_at);
CREATE INDEX ix_process_sample_values_point_time
  ON process_sample_values (point_key, occurred_at DESC);

INSERT INTO process_sample_frames (
  occurred_at, frame_id, event_id, recorded_at, ingested_at, edge_id, source,
  subject_type, subject_id, execution_id, phase_code, data_model_id, data_model_version)
SELECT DISTINCT ON (event_id)
  occurred_at, ingest_id, event_id, recorded_at, coalesce(ingested_at, recorded_at),
  edge_id, source, subject_type, subject_id, execution_id, phase_code,
  data_model_id, data_model_version
FROM time_series_samples
ORDER BY event_id, signal_code;

INSERT INTO process_sample_values (
  occurred_at, frame_id, point_key, quality_code,
  numeric_value, integer_value, boolean_value, text_value)
SELECT
  sample.occurred_at, sample.ingest_id, point.point_key,
  CASE sample.quality_code WHEN 'good' THEN 0 WHEN 'uncertain' THEN 1 ELSE 2 END,
  sample.numeric_value, sample.integer_value, sample.boolean_value, sample.text_value
FROM time_series_samples AS sample
JOIN collection_points AS point
  ON point.collection_point_id = sample.collection_point_id;

DROP TABLE time_series_samples;
