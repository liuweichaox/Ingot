ALTER TABLE platform_edges ADD COLUMN site_id text;

UPDATE platform_edges AS edge
SET site_id = source.site_id
FROM (
  SELECT edge_id, min(site_id) AS site_id
  FROM production_events
  GROUP BY edge_id
  HAVING count(DISTINCT site_id) = 1
) AS source
WHERE edge.edge_id = source.edge_id;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM platform_edges WHERE site_id IS NULL) THEN
    RAISE EXCEPTION
      'platform_edges contains rows without an unambiguous site; assign or recreate development data before applying 0006';
  END IF;
END $$;

ALTER TABLE platform_edges ALTER COLUMN site_id SET NOT NULL;
CREATE INDEX ix_platform_edges_site_seen ON platform_edges(site_id, last_seen_at DESC);

ALTER TABLE inspection_records ADD COLUMN site_id text;
ALTER TABLE inspection_scopes ADD COLUMN site_id text;
ALTER TABLE inspection_attachments ADD COLUMN site_id text;

WITH execution_sites AS (
  SELECT execution_id, min(site_id) AS site_id
  FROM production_events
  WHERE execution_id IS NOT NULL
  GROUP BY execution_id
  HAVING count(DISTINCT site_id) = 1
)
UPDATE inspection_records AS record
SET site_id = execution_sites.site_id
FROM execution_sites
WHERE execution_sites.execution_id = record.execution_id;

WITH execution_sites AS (
  SELECT execution_id, min(site_id) AS site_id
  FROM production_events
  WHERE execution_id IS NOT NULL
  GROUP BY execution_id
  HAVING count(DISTINCT site_id) = 1
)
UPDATE inspection_scopes AS scope
SET site_id = COALESCE(
  NULLIF(scope.payload->>'siteId', ''),
  NULLIF(scope.payload->'context'->>'site_id', ''),
  execution_sites.site_id)
FROM execution_sites
WHERE execution_sites.execution_id = scope.scope_id;

UPDATE inspection_scopes
SET site_id = COALESCE(
  NULLIF(payload->>'siteId', ''),
  NULLIF(payload->'context'->>'siteId', ''),
  NULLIF(payload->'context'->>'site_id', ''))
WHERE site_id IS NULL;

WITH attachment_sites AS (
  SELECT (item->>'attachmentId')::uuid AS attachment_id, min(record.site_id) AS site_id
  FROM inspection_records AS record
  CROSS JOIN LATERAL jsonb_array_elements(record.attachments) AS item
  WHERE item ? 'attachmentId' AND record.site_id IS NOT NULL
  GROUP BY (item->>'attachmentId')::uuid
  HAVING count(DISTINCT record.site_id) = 1
)
UPDATE inspection_attachments AS attachment
SET site_id = attachment_sites.site_id
FROM attachment_sites
WHERE attachment_sites.attachment_id = attachment.attachment_id;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM inspection_records WHERE site_id IS NULL) OR
     EXISTS (SELECT 1 FROM inspection_scopes WHERE site_id IS NULL) OR
     EXISTS (SELECT 1 FROM inspection_attachments WHERE site_id IS NULL) THEN
    RAISE EXCEPTION
      'inspection data contains rows without an unambiguous site; assign or recreate development data before applying 0006';
  END IF;
END $$;

ALTER TABLE inspection_records ALTER COLUMN site_id SET NOT NULL;
ALTER TABLE inspection_scopes ALTER COLUMN site_id SET NOT NULL;
ALTER TABLE inspection_attachments ALTER COLUMN site_id SET NOT NULL;

ALTER TABLE inspection_attachments DROP CONSTRAINT inspection_attachments_sha256_key;
ALTER TABLE inspection_attachments ADD CONSTRAINT inspection_attachments_site_sha256_key UNIQUE (site_id, sha256);

CREATE INDEX idx_inspection_records_site_time
  ON inspection_records(site_id, measured_at DESC, record_id DESC);
CREATE INDEX idx_inspection_scopes_site_time
  ON inspection_scopes(site_id, to_at DESC, scope_id);
CREATE INDEX idx_inspection_attachments_site
  ON inspection_attachments(site_id, attachment_id);
