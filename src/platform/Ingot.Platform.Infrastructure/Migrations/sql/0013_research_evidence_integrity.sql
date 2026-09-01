-- Make research evidence project-consistent and append-only without rewriting historical payloads.
-- Constraint names are intentionally short enough to remain unique under PostgreSQL's 63-byte identifier limit.
ALTER TABLE research_experiments
    ADD CONSTRAINT rr_experiments_project_experiment_uq
        UNIQUE (project_id, experiment_id);

ALTER TABLE research_experiment_results
    ADD CONSTRAINT rr_results_project_result_uq
        UNIQUE (project_id, result_id);

ALTER TABLE research_operating_regions
    ADD CONSTRAINT rr_regions_project_region_uq
        UNIQUE (project_id, operating_region_id);

ALTER TABLE research_operating_region_results
    ADD COLUMN project_id uuid;

UPDATE research_operating_region_results AS link
SET project_id = region.project_id
FROM research_operating_regions AS region
WHERE region.operating_region_id = link.operating_region_id;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM research_experiment_results AS result
        LEFT JOIN research_experiments AS experiment
            ON experiment.project_id = result.project_id
           AND experiment.experiment_id = result.experiment_id
        WHERE experiment.experiment_id IS NULL
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'research_experiment_results contains a project/experiment mismatch';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM research_operating_region_results
        WHERE project_id IS NULL
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'research_operating_region_results contains an unknown operating region';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM research_operating_region_results AS link
        LEFT JOIN research_experiment_results AS result
            ON result.project_id = link.project_id
           AND result.result_id = link.result_id
        WHERE result.result_id IS NULL
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'research_operating_region_results contains a cross-project result link';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM research_shadow_recommendations AS recommendation
        LEFT JOIN research_experiments AS experiment
            ON experiment.project_id = recommendation.project_id
           AND experiment.experiment_id = recommendation.experiment_id
        WHERE experiment.experiment_id IS NULL
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'research_shadow_recommendations contains a project/experiment mismatch';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM research_transfer_assessments AS assessment
        LEFT JOIN research_operating_regions AS region
            ON region.project_id = assessment.source_project_id
           AND region.operating_region_id = assessment.source_operating_region_id
        WHERE region.operating_region_id IS NULL
    ) THEN
        RAISE EXCEPTION USING
            ERRCODE = '23503',
            MESSAGE = 'research_transfer_assessments contains a source project/region mismatch';
    END IF;
END;
$$;

ALTER TABLE research_operating_region_results
    ALTER COLUMN project_id SET NOT NULL;

ALTER TABLE research_experiment_results
    ADD CONSTRAINT rr_results_project_experiment_fk
        FOREIGN KEY (project_id, experiment_id)
        REFERENCES research_experiments(project_id, experiment_id)
        ON DELETE NO ACTION
        NOT VALID;

ALTER TABLE research_operating_region_results
    ADD CONSTRAINT rr_region_results_project_region_fk
        FOREIGN KEY (project_id, operating_region_id)
        REFERENCES research_operating_regions(project_id, operating_region_id)
        ON DELETE NO ACTION
        NOT VALID,
    ADD CONSTRAINT rr_region_results_project_result_fk
        FOREIGN KEY (project_id, result_id)
        REFERENCES research_experiment_results(project_id, result_id)
        ON DELETE NO ACTION
        NOT VALID;

ALTER TABLE research_shadow_recommendations
    ADD CONSTRAINT rr_shadow_project_recommendation_uq
        UNIQUE (project_id, recommendation_id),
    ADD CONSTRAINT rr_shadow_project_experiment_fk
        FOREIGN KEY (project_id, experiment_id)
        REFERENCES research_experiments(project_id, experiment_id)
        ON DELETE NO ACTION
        NOT VALID;

ALTER TABLE research_transfer_assessments
    ADD CONSTRAINT rr_transfer_source_project_region_fk
        FOREIGN KEY (source_project_id, source_operating_region_id)
        REFERENCES research_operating_regions(project_id, operating_region_id)
        ON DELETE NO ACTION
        NOT VALID;

CREATE TABLE research_shadow_recommendation_outcomes (
    recommendation_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    payload jsonb NOT NULL,
    materialized_at timestamp with time zone NOT NULL,
    CONSTRAINT rr_shadow_outcomes_recommendation_fk
        FOREIGN KEY (recommendation_id)
        REFERENCES research_shadow_recommendations(recommendation_id)
        ON DELETE NO ACTION,
    CONSTRAINT rr_shadow_outcomes_project_recommendation_fk
        FOREIGN KEY (project_id, recommendation_id)
        REFERENCES research_shadow_recommendations(project_id, recommendation_id)
        ON DELETE NO ACTION
        NOT VALID
);

INSERT INTO research_shadow_recommendation_outcomes
    (recommendation_id, project_id, payload, materialized_at)
SELECT recommendation_id,
       project_id,
       payload -> 'outcome',
       COALESCE((payload -> 'outcome' ->> 'capturedAt')::timestamptz, decided_at)
FROM research_shadow_recommendations
WHERE payload -> 'outcome' IS NOT NULL
  AND payload -> 'outcome' <> 'null'::jsonb;

UPDATE research_shadow_recommendations
SET payload = payload - 'outcome'
WHERE payload ? 'outcome';

ALTER TABLE research_experiment_results
    VALIDATE CONSTRAINT rr_results_project_experiment_fk;

ALTER TABLE research_operating_region_results
    VALIDATE CONSTRAINT rr_region_results_project_region_fk;

ALTER TABLE research_operating_region_results
    VALIDATE CONSTRAINT rr_region_results_project_result_fk;

ALTER TABLE research_shadow_recommendations
    VALIDATE CONSTRAINT rr_shadow_project_experiment_fk;

ALTER TABLE research_transfer_assessments
    VALIDATE CONSTRAINT rr_transfer_source_project_region_fk;

ALTER TABLE research_shadow_recommendation_outcomes
    VALIDATE CONSTRAINT rr_shadow_outcomes_project_recommendation_fk;

CREATE OR REPLACE FUNCTION reject_shadow_recommendation_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '影子评估证据只能追加，不能更新或删除。';
END;
$$;

CREATE TRIGGER research_shadow_recommendations_append_only
    BEFORE UPDATE OR DELETE ON research_shadow_recommendations
    FOR EACH ROW EXECUTE FUNCTION reject_shadow_recommendation_mutation();

CREATE TRIGGER research_shadow_recommendation_outcomes_append_only
    BEFORE UPDATE OR DELETE ON research_shadow_recommendation_outcomes
    FOR EACH ROW EXECUTE FUNCTION reject_shadow_recommendation_mutation();
