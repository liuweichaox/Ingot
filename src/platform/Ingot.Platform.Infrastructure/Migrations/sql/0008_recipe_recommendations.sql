-- Store normal-production recipe recommendations independently from controlled experiments.
CREATE TABLE research_recipe_recommendations (
    recommendation_id uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
    input_hash text NOT NULL,
    payload jsonb NOT NULL,
    generated_at timestamp with time zone NOT NULL,
    CONSTRAINT research_recipe_recommendations_input_hash_check
        CHECK (input_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX ux_research_recipe_recommendations_project_input
    ON research_recipe_recommendations(project_id, input_hash);

CREATE INDEX ix_research_recipe_recommendations_project_page
    ON research_recipe_recommendations(project_id, generated_at DESC, recommendation_id DESC);

CREATE TABLE recipe_recommendation_knowledge_usage (
    recommendation_id uuid NOT NULL
        REFERENCES research_recipe_recommendations(recommendation_id) ON DELETE CASCADE,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    usage_type text NOT NULL,
    content_hash text NOT NULL,
    CONSTRAINT recipe_recommendation_knowledge_usage_pkey
        PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type),
    CONSTRAINT recipe_recommendation_knowledge_usage_hash_check
        CHECK (content_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT recipe_recommendation_knowledge_usage_claim_version_fkey
        FOREIGN KEY (claim_id, claim_version)
        REFERENCES mechanism_claim_versions(claim_id, version)
);
