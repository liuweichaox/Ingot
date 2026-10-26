ALTER TABLE public.agent_runs
  ADD COLUMN next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE INDEX ix_agent_runs_retry_schedule
  ON public.agent_runs(status, next_attempt_at, created_at, run_id)
  WHERE status IN ('queued', 'running', 'cancelling');
