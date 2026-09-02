-- Scope equipment-bound manufacturing context by site to prevent same-named equipment from colliding.
ALTER TABLE tooling_installations
    ADD COLUMN site_id text;

ALTER TABLE production_contexts
    ADD COLUMN site_id text;

UPDATE tooling_installations
SET site_id = 'legacy',
    payload = jsonb_set(payload, '{siteId}', to_jsonb('legacy'::text), true)
WHERE site_id IS NULL;

UPDATE production_contexts
SET site_id = 'legacy',
    payload = jsonb_set(payload, '{siteId}', to_jsonb('legacy'::text), true)
WHERE site_id IS NULL;

ALTER TABLE tooling_installations
    ALTER COLUMN site_id SET NOT NULL;

ALTER TABLE production_contexts
    ALTER COLUMN site_id SET NOT NULL;

DROP INDEX idx_tooling_installations_active_equipment;
CREATE UNIQUE INDEX idx_tooling_installations_active_equipment
    ON tooling_installations(site_id, equipment_id)
    WHERE removed_at IS NULL;

DROP INDEX idx_tooling_installations_equipment_time;
CREATE INDEX idx_tooling_installations_equipment_time
    ON tooling_installations(site_id, equipment_id, installed_at, removed_at);

DROP INDEX idx_production_contexts_active_equipment;
CREATE UNIQUE INDEX idx_production_contexts_active_equipment
    ON production_contexts(site_id, equipment_id)
    WHERE valid_to IS NULL;

DROP INDEX idx_production_contexts_equipment_time;
CREATE INDEX idx_production_contexts_equipment_time
    ON production_contexts(site_id, equipment_id, valid_from, valid_to);

ALTER TABLE tooling_installations
    ADD CONSTRAINT tooling_installations_installation_id_site_id_key
    UNIQUE (installation_id, site_id);

ALTER TABLE production_contexts
    ADD CONSTRAINT production_contexts_tooling_installation_site_id_fkey
    FOREIGN KEY (tooling_installation_id, site_id)
    REFERENCES tooling_installations(installation_id, site_id);
