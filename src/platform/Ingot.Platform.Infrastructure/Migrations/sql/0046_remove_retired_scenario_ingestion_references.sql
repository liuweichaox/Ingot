-- Remove obsolete scenario-package references to retired Ingot-managed acquisition tasks.
UPDATE public.scenario_packages
SET payload = payload - 'ingestionTasks' - 'IngestionTasks' - 'ingestion_tasks'
WHERE payload ? 'ingestionTasks'
   OR payload ? 'IngestionTasks'
   OR payload ? 'ingestion_tasks';
