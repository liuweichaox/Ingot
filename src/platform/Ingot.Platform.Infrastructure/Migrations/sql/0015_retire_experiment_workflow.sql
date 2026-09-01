-- Retire the removed workflow while preserving its historical records for read-only audit.
CREATE TABLE research_retired_workflow_records (
    record_kind text NOT NULL,
    record_id text NOT NULL,
    project_id uuid,
    payload jsonb NOT NULL,
    retired_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (record_kind, record_id)
);

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-run-plan', experiment_id::text, project_id, payload
FROM research_experiments
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-run-plan-entry', run.experiment_id::text || ':' || run.execution_key,
       plan.project_id,
       jsonb_build_object('executionKey', run.execution_key, 'sequence', run.sequence, 'run', run.payload)
FROM research_experiment_runs AS run
JOIN research_experiments AS plan ON plan.experiment_id = run.experiment_id
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-analysis', result_id::text, project_id, payload
FROM research_experiment_results
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-advisory', recommendation_id::text, project_id, payload
FROM research_shadow_recommendations
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-advisory-outcome', recommendation_id::text, project_id, payload
FROM research_shadow_recommendation_outcomes
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-advisory-execution', recommendation_id::text, project_id,
       jsonb_build_object(
           'recommendationId', recommendation_id,
           'actualExecutionKey', actual_execution_key,
           'linkedAt', linked_at)
FROM research_shadow_recommendation_executions
ON CONFLICT DO NOTHING;

INSERT INTO research_retired_workflow_records(record_kind, record_id, project_id, payload)
SELECT 'retired-region-link', operating_region_id::text || ':' || result_id::text, project_id,
       jsonb_build_object('operatingRegionId', operating_region_id, 'resultId', result_id)
FROM research_operating_region_results
ON CONFLICT DO NOTHING;

ALTER TABLE recommendation_knowledge_usage
    DROP CONSTRAINT IF EXISTS fk_recommendation_knowledge_usage_experiment;

INSERT INTO research_retired_workflow_records(record_kind, record_id, payload)
SELECT 'retired-knowledge-usage', recommendation_id::text || ':' || claim_id::text || ':' || claim_version::text,
       to_jsonb(usage)
FROM recommendation_knowledge_usage AS usage
WHERE NOT EXISTS (
    SELECT 1
    FROM research_recipe_recommendations AS recommendation
    WHERE recommendation.recommendation_id = usage.recommendation_id)
ON CONFLICT DO NOTHING;

DELETE FROM recommendation_knowledge_usage AS usage
WHERE NOT EXISTS (
    SELECT 1
    FROM research_recipe_recommendations AS recommendation
    WHERE recommendation.recommendation_id = usage.recommendation_id);

ALTER TABLE recommendation_knowledge_usage
    ADD CONSTRAINT recommendation_knowledge_usage_recipe_fk
    FOREIGN KEY (recommendation_id)
    REFERENCES research_recipe_recommendations(recommendation_id)
    ON DELETE CASCADE;

DROP TABLE research_shadow_recommendation_executions;
DROP TABLE research_shadow_recommendation_outcomes;
DROP TABLE research_shadow_recommendations;
DROP TABLE research_operating_region_results;
DROP TABLE research_experiment_results;
DROP TABLE research_experiment_runs;
DROP TABLE research_experiments;
