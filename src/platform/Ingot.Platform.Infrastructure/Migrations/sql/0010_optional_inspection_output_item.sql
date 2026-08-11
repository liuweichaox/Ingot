-- ExecutionId is the mandatory quality join. Some manufacturing processes do
-- not assign a stable ID to every physical output item, so that secondary trace key
-- must not force clients to fabricate one.
ALTER TABLE inspection_records
  ALTER COLUMN output_item_id DROP NOT NULL;
