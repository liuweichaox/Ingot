-- Preserve both policy values so freshness history can distinguish policy changes from sampling recovery.
ALTER TABLE public.edge_task_freshness_transitions
    ADD COLUMN previous_threshold_ms integer NULL;
