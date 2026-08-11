ALTER TABLE execution_analysis_materializations
  ADD COLUMN IF NOT EXISTS source_min_ingest_id BIGINT NOT NULL DEFAULT 0;

ALTER TABLE execution_analysis_materializations
  ADD COLUMN IF NOT EXISTS source_content_hash TEXT NOT NULL DEFAULT '';
