-- Historical backlog counts without an observation time remain unverified.
ALTER TABLE public.edge_runtime_status_history
    ADD COLUMN backlog_observed_at TIMESTAMPTZ NULL;
