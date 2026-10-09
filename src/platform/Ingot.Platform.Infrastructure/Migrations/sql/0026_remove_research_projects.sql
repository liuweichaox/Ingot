-- 删除研发项目及其假设、操作域、知识声明和审计；下一配方建议、机理知识与知识来源改为按站点和配方隔离。
-- 历史行无法可靠地从项目重新归属到站点与配方，因此存在任何项目范围数据时拒绝迁移。
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM process_research_projects)
       OR EXISTS (SELECT 1 FROM research_recipe_recommendations)
       OR EXISTS (SELECT 1 FROM mechanism_claims)
       OR EXISTS (SELECT 1 FROM mechanism_claim_conflicts)
       OR EXISTS (SELECT 1 FROM knowledge_sources) THEN
        RAISE EXCEPTION
            '0026_remove_research_projects: project-scoped recommendation, mechanism or knowledge rows exist; export and clear them before upgrading.';
    END IF;
END $$;

ALTER TABLE mechanism_claim_lifecycle_decisions
    DROP CONSTRAINT mechanism_claim_lifecycle_evaluation_check,
    DROP CONSTRAINT mechanism_claim_lifecycle_validation_hypothesis_fkey,
    DROP COLUMN validation_hypothesis_id,
    ADD CONSTRAINT mechanism_claim_lifecycle_evaluation_check CHECK (
        to_status IN ('supported', 'validated')
            AND evaluation_outcome = 'supports' AND evaluation_summary IS NOT NULL
        OR to_status = 'falsified'
            AND evaluation_outcome = 'falsifies' AND evaluation_summary IS NOT NULL
        OR to_status NOT IN ('supported', 'validated', 'falsified')
            AND evaluation_outcome IS NULL AND evaluation_summary IS NULL);

DROP TABLE research_hypothesis_interaction_variables,
    research_hypothesis_interactions,
    research_hypothesis_causal_links,
    research_hypothesis_confounders,
    research_hypothesis_evidence,
    research_hypothesis_failure_conditions,
    research_hypothesis_falsification_conditions,
    research_hypothesis_temporal_features,
    research_hypothesis_variables,
    research_hypotheses,
    research_operating_regions,
    research_knowledge_claims,
    research_evidence,
    process_research_audit,
    research_project_members CASCADE;

-- 建议证据链：去掉项目维度，改为站点 + 配方，决定链路的外键改为单列。
ALTER TABLE research_recipe_recommendation_decision_outcomes
    DROP CONSTRAINT rr_decision_outcomes_project_decision_fk,
    DROP COLUMN project_id,
    ADD COLUMN materialized_by text NOT NULL;
ALTER TABLE research_recipe_recommendation_decision_executions
    DROP CONSTRAINT rr_decision_executions_project_decision_fk,
    DROP CONSTRAINT rr_decision_executions_project_decision_uq,
    DROP CONSTRAINT rr_decision_executions_project_execution_uq,
    DROP COLUMN project_id,
    ADD COLUMN linked_by text NOT NULL,
    ADD CONSTRAINT rr_decision_executions_execution_uq UNIQUE (actual_execution_key);
ALTER TABLE research_recipe_recommendation_decisions
    DROP CONSTRAINT rr_decisions_project_recommendation_fk,
    DROP CONSTRAINT rr_decisions_project_decision_uq,
    DROP COLUMN project_id,
    ADD COLUMN site_code text NOT NULL,
    ADD COLUMN process_specification_id text NOT NULL;
ALTER TABLE research_recipe_recommendations
    DROP CONSTRAINT rr_recommendations_project_fk,
    DROP CONSTRAINT rr_recommendations_project_recommendation_uq,
    DROP COLUMN project_id,
    ADD COLUMN site_code text NOT NULL,
    ADD COLUMN process_specification_id text NOT NULL,
    ADD CONSTRAINT rr_recommendations_scope_recommendation_uq
        UNIQUE (site_code, process_specification_id, recommendation_id);

CREATE UNIQUE INDEX ux_rr_recommendations_scope_input
    ON research_recipe_recommendations(site_code, process_specification_id, input_hash);
CREATE INDEX ix_rr_recommendations_scope_page
    ON research_recipe_recommendations(site_code, process_specification_id, generated_at DESC, recommendation_id DESC);
CREATE INDEX ix_rr_recommendations_page
    ON research_recipe_recommendations(generated_at DESC, recommendation_id DESC);

ALTER TABLE research_recipe_recommendation_decisions
    ADD CONSTRAINT rr_decisions_scope_recommendation_fk
        FOREIGN KEY (site_code, process_specification_id, recommendation_id)
        REFERENCES research_recipe_recommendations(site_code, process_specification_id, recommendation_id);
CREATE INDEX ix_rr_decisions_scope_pending
    ON research_recipe_recommendation_decisions(site_code, process_specification_id, decided_at, decision_id)
    WHERE decision IN ('accepted', 'modified');
ALTER TABLE research_recipe_recommendation_decision_executions
    ADD CONSTRAINT rr_decision_executions_decision_fk
        FOREIGN KEY (decision_id) REFERENCES research_recipe_recommendation_decisions(decision_id);
ALTER TABLE research_recipe_recommendation_decision_outcomes
    ADD CONSTRAINT rr_decision_outcomes_execution_fk
        FOREIGN KEY (decision_id) REFERENCES research_recipe_recommendation_decision_executions(decision_id);

-- 机理知识：每条声明属于一个站点下的一个配方，冲突只能发生在同一范围内。
ALTER TABLE mechanism_claim_conflicts
    DROP CONSTRAINT fk_mechanism_conflict_left_project,
    DROP CONSTRAINT fk_mechanism_conflict_right_project,
    DROP CONSTRAINT mechanism_claim_conflicts_project_id_fkey;
DROP INDEX ux_mechanism_conflict_pair;
DROP INDEX ix_mechanism_claim_conflicts_project;
ALTER TABLE mechanism_claim_conflicts
    DROP COLUMN project_id,
    ADD COLUMN site_code text NOT NULL,
    ADD COLUMN process_specification_id text NOT NULL;
ALTER TABLE mechanism_claims
    DROP CONSTRAINT mechanism_claims_project_id_fkey,
    DROP CONSTRAINT ux_mechanism_claims_id_project;
DROP INDEX ix_mechanism_claims_project_status;
ALTER TABLE mechanism_claims
    DROP COLUMN project_id,
    ADD COLUMN site_code text NOT NULL,
    ADD COLUMN process_specification_id text NOT NULL,
    ADD CONSTRAINT ux_mechanism_claims_id_scope UNIQUE (claim_id, site_code, process_specification_id);
CREATE INDEX ix_mechanism_claims_scope_status
    ON mechanism_claims(site_code, process_specification_id, status, updated_at DESC);
ALTER TABLE mechanism_claim_conflicts
    ADD CONSTRAINT fk_mechanism_conflict_left_scope
        FOREIGN KEY (left_claim_id, site_code, process_specification_id)
        REFERENCES mechanism_claims(claim_id, site_code, process_specification_id),
    ADD CONSTRAINT fk_mechanism_conflict_right_scope
        FOREIGN KEY (right_claim_id, site_code, process_specification_id)
        REFERENCES mechanism_claims(claim_id, site_code, process_specification_id);
CREATE INDEX ix_mechanism_claim_conflicts_scope
    ON mechanism_claim_conflicts(site_code, process_specification_id, status, created_at DESC);
CREATE UNIQUE INDEX ux_mechanism_conflict_pair
    ON mechanism_claim_conflicts(
        site_code,
        process_specification_id,
        LEAST(left_claim_id, right_claim_id),
        GREATEST(left_claim_id, right_claim_id),
        (CASE WHEN left_claim_id < right_claim_id THEN left_claim_version ELSE right_claim_version END),
        (CASE WHEN left_claim_id < right_claim_id THEN right_claim_version ELSE left_claim_version END),
        conflict_kind)
    WHERE status = 'open';

-- 知识来源：按站点去重与检索。
ALTER TABLE knowledge_sources
    DROP CONSTRAINT fk_knowledge_sources_project,
    DROP CONSTRAINT ux_knowledge_sources_project_sha256;
DROP INDEX ix_knowledge_sources_project;
DROP INDEX ix_knowledge_sources_project_reviewed;
ALTER TABLE knowledge_sources
    DROP COLUMN project_id,
    ADD COLUMN site_code text NOT NULL,
    ADD CONSTRAINT ux_knowledge_sources_site_sha256 UNIQUE (site_code, sha256);
CREATE INDEX ix_knowledge_sources_site
    ON knowledge_sources(site_code, updated_at DESC);
CREATE INDEX ix_knowledge_sources_site_reviewed
    ON knowledge_sources(site_code, updated_at DESC) WHERE status = 'reviewed';

DROP TABLE process_research_projects;
