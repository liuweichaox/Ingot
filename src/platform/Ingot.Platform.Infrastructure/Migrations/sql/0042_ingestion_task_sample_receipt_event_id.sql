-- Index exact event receipt correlation for ingestion-task sample verification.
CREATE INDEX idx_production_events_ingestion_task_receipt_event_id
    ON public.production_events (site_id, edge_id, event_id)
    WHERE configuration_kind = 'ingestion-task';
