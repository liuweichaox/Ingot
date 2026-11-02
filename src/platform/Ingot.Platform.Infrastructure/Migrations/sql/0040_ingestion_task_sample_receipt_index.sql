-- Speed up site- and Edge-scoped receipt lookups for a specific ingestion-task version.
CREATE INDEX idx_production_events_ingestion_task_receipt
    ON public.production_events (
        site_id,
        edge_id,
        configuration_id,
        configuration_version,
        event_type,
        ingested_at DESC,
        ingest_id DESC)
    WHERE configuration_kind = 'ingestion-task';
