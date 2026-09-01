-- Preserve the daily decision, actual execution association, and source outcome as separate append-only evidence.
-- Constraint names are intentionally short enough to remain unique under PostgreSQL's 63-byte identifier limit.
ALTER TABLE research_recipe_recommendations
    DROP CONSTRAINT research_recipe_recommendations_project_id_fkey,
    ADD CONSTRAINT rr_recommendations_project_fk
        FOREIGN KEY (project_id)
        REFERENCES process_research_projects(project_id)
        ON DELETE NO ACTION,
    ADD CONSTRAINT rr_recommendations_project_recommendation_uq
        UNIQUE (project_id, recommendation_id);

CREATE TABLE research_recipe_recommendation_decisions (
    decision_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    recommendation_id uuid NOT NULL,
    recommendation_key text NOT NULL,
    decision text NOT NULL,
    payload jsonb NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    CONSTRAINT rr_decisions_status_ck
        CHECK (decision IN ('accepted', 'modified', 'rejected')),
    CONSTRAINT rr_decisions_recommendation_item_uq
        UNIQUE (recommendation_id, recommendation_key),
    CONSTRAINT rr_decisions_project_decision_uq
        UNIQUE (project_id, decision_id),
    CONSTRAINT rr_decisions_project_recommendation_fk
        FOREIGN KEY (project_id, recommendation_id)
        REFERENCES research_recipe_recommendations(project_id, recommendation_id)
        ON DELETE NO ACTION
);

CREATE INDEX ix_rr_decisions_project_page
    ON research_recipe_recommendation_decisions(project_id, decided_at DESC, decision_id DESC);

CREATE TABLE research_recipe_recommendation_decision_executions (
    decision_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    actual_execution_key text NOT NULL,
    linked_at timestamp with time zone NOT NULL,
    CONSTRAINT rr_decision_executions_project_decision_uq
        UNIQUE (project_id, decision_id),
    CONSTRAINT rr_decision_executions_project_execution_uq
        UNIQUE (project_id, actual_execution_key),
    CONSTRAINT rr_decision_executions_project_decision_fk
        FOREIGN KEY (project_id, decision_id)
        REFERENCES research_recipe_recommendation_decisions(project_id, decision_id)
        ON DELETE NO ACTION
);

CREATE TABLE research_recipe_recommendation_decision_outcomes (
    decision_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    payload jsonb NOT NULL,
    materialized_at timestamp with time zone NOT NULL,
    CONSTRAINT rr_decision_outcomes_project_decision_fk
        FOREIGN KEY (project_id, decision_id)
        REFERENCES research_recipe_recommendation_decision_executions(project_id, decision_id)
        ON DELETE NO ACTION
);

CREATE OR REPLACE FUNCTION reject_recipe_recommendation_evidence_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '日常配方决定证据只能追加，不能更新或删除。';
END;
$$;

CREATE TRIGGER research_recipe_recommendation_decisions_append_only
    BEFORE UPDATE OR DELETE ON research_recipe_recommendation_decisions
    FOR EACH ROW EXECUTE FUNCTION reject_recipe_recommendation_evidence_mutation();

CREATE TRIGGER research_recipe_recommendation_decision_executions_append_only
    BEFORE UPDATE OR DELETE ON research_recipe_recommendation_decision_executions
    FOR EACH ROW EXECUTE FUNCTION reject_recipe_recommendation_evidence_mutation();

CREATE TRIGGER research_recipe_recommendation_decision_outcomes_append_only
    BEFORE UPDATE OR DELETE ON research_recipe_recommendation_decision_outcomes
    FOR EACH ROW EXECUTE FUNCTION reject_recipe_recommendation_evidence_mutation();
