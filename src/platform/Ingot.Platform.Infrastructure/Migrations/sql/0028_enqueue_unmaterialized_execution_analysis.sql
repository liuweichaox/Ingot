-- Event ingestion previously queued recomputation only for executions that already had a
-- materialization, so completed executions without one never left the pending state.
INSERT INTO execution_analysis_recompute_jobs(
    execution_id, invalidated_source_max_ingest_id, reason, status, available_at, updated_at)
SELECT pe.execution_id, max(pe.ingest_id), 'initial_materialization', 'queued', now(), now()
FROM production_events pe
WHERE pe.execution_id IS NOT NULL
  AND EXISTS (
    SELECT 1 FROM production_events completed
    WHERE completed.execution_id = pe.execution_id
      AND completed.event_type = 'process.execution.completed')
  AND NOT EXISTS (
    SELECT 1 FROM execution_analysis_materializations m
    WHERE m.execution_id = pe.execution_id AND m.status = 'ready')
GROUP BY pe.execution_id
ON CONFLICT (execution_id) DO NOTHING;
