ALTER TABLE knowledge_extraction_jobs
  DROP CONSTRAINT knowledge_extraction_jobs_status_check;

ALTER TABLE knowledge_extraction_jobs
  ADD CONSTRAINT knowledge_extraction_jobs_status_check
  CHECK (status IN ('queued', 'running', 'completed', 'failed', 'dead-letter'));

CREATE INDEX ix_knowledge_extraction_jobs_running_lease
  ON knowledge_extraction_jobs(leased_at)
  WHERE status = 'running';

UPDATE execution_analysis_backfill_jobs
SET status = 'queued',
    payload = jsonb_set(payload, '{status}', '"queued"'::jsonb, true),
    updated_at = now()
WHERE status = 'running';

ALTER TABLE execution_analysis_backfill_jobs
  ADD COLUMN available_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  ADD COLUMN attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  ADD COLUMN lease_id UUID,
  ADD COLUMN leased_at TIMESTAMPTZ,
  ADD CONSTRAINT execution_analysis_backfill_jobs_lease_check
    CHECK ((status = 'running') = (lease_id IS NOT NULL AND leased_at IS NOT NULL));

CREATE INDEX ix_execution_analysis_backfill_jobs_claim
  ON execution_analysis_backfill_jobs(status, available_at, created_at);

CREATE TABLE execution_analysis_recompute_jobs (
  execution_id TEXT PRIMARY KEY,
  invalidated_source_max_ingest_id BIGINT NOT NULL,
  reason TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('queued', 'running')),
  available_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  lease_id UUID,
  leased_at TIMESTAMPTZ,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CHECK ((status = 'running') = (lease_id IS NOT NULL AND leased_at IS NOT NULL))
);

CREATE INDEX ix_execution_analysis_recompute_jobs_claim
  ON execution_analysis_recompute_jobs(status, available_at, updated_at);

INSERT INTO execution_analysis_recompute_jobs(
  execution_id, invalidated_source_max_ingest_id, reason, status)
SELECT execution_id, COALESCE(invalidated_source_max_ingest_id, 0),
       COALESCE(invalidation_reason, 'migration-dirty-materialization'), 'queued'
FROM execution_analysis_materializations
WHERE status = 'dirty'
ON CONFLICT (execution_id) DO NOTHING;
