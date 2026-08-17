-- Typed time_series_samples rows are the only source of process values.
-- production_events keeps lifecycle and business events only.

DROP VIEW IF EXISTS production_event_stream;
DROP VIEW IF EXISTS projected_process_sample_events;

ALTER TABLE time_series_samples
  ADD COLUMN IF NOT EXISTS ingested_at TIMESTAMPTZ;

-- No legacy sample-event fallback is retained.
DELETE FROM production_events
WHERE event_type = 'process.sample';
