-- Separate the immutable shadow engineer choice from its later real-run association.
ALTER TABLE research_shadow_recommendations
    DROP CONSTRAINT research_shadow_recommendations_project_actual_execution_key_ke,
    ALTER COLUMN actual_execution_key DROP NOT NULL;

CREATE TABLE research_shadow_recommendation_executions (
    recommendation_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    actual_execution_key text NOT NULL,
    linked_at timestamp with time zone NOT NULL,
    CONSTRAINT rr_shadow_executions_project_recommendation_uq
        UNIQUE (project_id, recommendation_id),
    CONSTRAINT rr_shadow_executions_project_execution_uq
        UNIQUE (project_id, actual_execution_key),
    CONSTRAINT rr_shadow_executions_project_recommendation_fk
        FOREIGN KEY (project_id, recommendation_id)
        REFERENCES research_shadow_recommendations(project_id, recommendation_id)
        ON DELETE NO ACTION
);

INSERT INTO research_shadow_recommendation_executions
    (recommendation_id, project_id, actual_execution_key, linked_at)
SELECT recommendation_id, project_id, actual_execution_key, decided_at
FROM research_shadow_recommendations
WHERE actual_execution_key IS NOT NULL;

CREATE TRIGGER research_shadow_recommendation_executions_append_only
    BEFORE UPDATE OR DELETE ON research_shadow_recommendation_executions
    FOR EACH ROW EXECUTE FUNCTION reject_shadow_recommendation_mutation();
