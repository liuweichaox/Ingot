-- Execution context is stored once in operation_context_snapshots and referenced by
-- time_series_samples.execution_id. Repeating the same JSON document for every signal
-- row multiplied storage by the number of signals in each process.sample event.

DROP INDEX IF EXISTS ix_time_series_samples_context;

ALTER TABLE time_series_samples
  DROP COLUMN IF EXISTS run_context;
