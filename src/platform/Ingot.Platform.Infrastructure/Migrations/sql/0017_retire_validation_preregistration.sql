-- Retire the removed validation preregistration workflow.
UPDATE process_research_projects
SET status = 'active',
    payload = jsonb_set(payload, '{status}', to_jsonb('active'::text), true),
    updated_at = now()
WHERE status = 'validating';

ALTER TABLE process_research_projects
    DROP CONSTRAINT process_research_projects_status_check;

ALTER TABLE process_research_projects
    ADD CONSTRAINT process_research_projects_status_check
    CHECK (status = ANY (ARRAY['draft', 'active', 'completed', 'archived']));

DROP TABLE IF EXISTS research_validation_preregistrations;
