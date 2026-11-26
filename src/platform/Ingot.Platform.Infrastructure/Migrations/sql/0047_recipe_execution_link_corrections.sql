ALTER TABLE research_recipe_recommendation_decision_executions
    DROP CONSTRAINT rr_decision_executions_execution_uq;

CREATE TABLE research_recipe_recommendation_execution_corrections (
    correction_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    decision_id uuid NOT NULL REFERENCES research_recipe_recommendation_decision_executions(decision_id),
    actual_execution_key text NOT NULL,
    linked_by text NOT NULL,
    linked_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX rr_execution_corrections_decision_idx
    ON research_recipe_recommendation_execution_corrections(decision_id, correction_id DESC);

CREATE TRIGGER research_recipe_recommendation_execution_corrections_append_only
    BEFORE UPDATE OR DELETE ON research_recipe_recommendation_execution_corrections
    FOR EACH ROW EXECUTE FUNCTION reject_recipe_recommendation_evidence_mutation();
