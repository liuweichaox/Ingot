-- Keep receipt lookups indexed on both persisted sample frames and custom event payloads.
DROP INDEX IF EXISTS public.idx_production_events_ingestion_task_receipt;

CREATE INDEX idx_production_events_ingestion_task_receipt
    ON public.production_events (
        site_id,
        edge_id,
        configuration_id,
        configuration_version,
        event_type,
        subject_type,
        subject_id,
        ingested_at DESC,
        ingest_id DESC)
    WHERE configuration_kind = 'ingestion-task';

CREATE INDEX idx_process_sample_frames_ingestion_task_receipt
    ON public.process_sample_frames (
        site_id,
        edge_id,
        configuration_kind,
        configuration_id,
        configuration_version,
        subject_type,
        subject_id,
        ingested_at DESC,
        frame_id DESC)
    WHERE configuration_kind = 'ingestion-task';
