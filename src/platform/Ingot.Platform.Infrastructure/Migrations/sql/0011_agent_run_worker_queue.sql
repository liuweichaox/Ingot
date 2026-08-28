ALTER TABLE public.agent_runs
  ADD COLUMN lease_owner TEXT NULL,
  ADD COLUMN lease_expires_at TIMESTAMPTZ NULL,
  ADD COLUMN attempt_count INTEGER NOT NULL DEFAULT 0,
  ADD CONSTRAINT ck_agent_run_attempt_count CHECK (attempt_count >= 0),
  ADD CONSTRAINT ck_agent_run_lease_pair CHECK (
    (lease_owner IS NULL AND lease_expires_at IS NULL) OR
    (lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)
  );

CREATE INDEX ix_agent_runs_queue_claim
  ON public.agent_runs(status, lease_expires_at, created_at, run_id)
  WHERE status IN ('queued', 'running');
