ALTER TABLE cycle_analysis_materializations
  ADD COLUMN IF NOT EXISTS source_min_ingest_id BIGINT NOT NULL DEFAULT 0;

ALTER TABLE cycle_analysis_materializations
  ADD COLUMN IF NOT EXISTS source_content_hash TEXT NOT NULL DEFAULT '';
