-- Scope operation context snapshots by site so equal execution identifiers in
-- separate production cells cannot reuse each other's captured context.

ALTER TABLE operation_context_snapshots
    ADD COLUMN site_id text;

UPDATE operation_context_snapshots AS snapshot
SET site_id = source.site_id
FROM (
    SELECT execution_id, min(site_id) AS site_id
    FROM (
        SELECT execution_id, site_id
        FROM production_events
        WHERE execution_id IS NOT NULL
        UNION
        SELECT execution_id, site_id
        FROM process_sample_frames
        WHERE execution_id IS NOT NULL
    ) AS observed
    GROUP BY execution_id
    HAVING count(DISTINCT site_id) = 1
) AS source
WHERE source.execution_id = snapshot.execution_id;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM operation_context_snapshots WHERE site_id IS NULL) THEN
        RAISE EXCEPTION
            'Cannot safely infer site_id for one or more operation context snapshots; resolve ambiguous legacy execution identifiers before retrying migration 0007.';
    END IF;
END
$$;

ALTER TABLE operation_context_snapshots
    ALTER COLUMN site_id SET NOT NULL;

ALTER TABLE operation_context_snapshots
    DROP CONSTRAINT operation_context_snapshots_pkey;

ALTER TABLE operation_context_snapshots
    ADD CONSTRAINT operation_context_snapshots_pkey PRIMARY KEY (site_id, execution_id);
