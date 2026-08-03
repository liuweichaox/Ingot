-- OperationRunId is the mandatory quality join. Some manufacturing processes do
-- not assign a stable ID to every physical workpiece, so that secondary trace key
-- must not force clients to fabricate one.
ALTER TABLE inspection_records
  ALTER COLUMN workpiece_id DROP NOT NULL;
