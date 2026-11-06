-- Remove retired acquisition configuration and task-specific freshness/deployment history.
DROP TABLE IF EXISTS public.edge_task_freshness_transitions;
DROP TABLE IF EXISTS public.edge_task_freshness_current;
DROP TABLE IF EXISTS public.edge_task_deployment_observation_state;
DROP TABLE IF EXISTS public.edge_task_deployment_observations;
DROP INDEX IF EXISTS public.idx_production_events_ingestion_task_receipt_event_id;
DROP INDEX IF EXISTS public.idx_process_sample_frames_ingestion_task_receipt;
DROP INDEX IF EXISTS public.idx_production_events_ingestion_task_receipt;

DROP TABLE IF EXISTS ingot_legacy.ingestion_task_bindings;
DROP TABLE IF EXISTS ingot_legacy.data_source_instances;
DROP TABLE IF EXISTS ingot_legacy.ingestion_task_templates;
DROP TABLE IF EXISTS ingot_legacy.acquisition_probe_tasks;
DROP TABLE IF EXISTS ingot_legacy.ingestion_tasks;
DROP SCHEMA IF EXISTS ingot_legacy;
