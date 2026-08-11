CREATE TABLE IF NOT EXISTS research_shadow_recommendations (
  recommendation_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  experiment_id UUID NOT NULL REFERENCES research_experiments(experiment_id),
  suggestion_execution_key TEXT NOT NULL,
  actual_execution_key TEXT NOT NULL,
  decision TEXT NOT NULL,
  payload JSONB NOT NULL,
  decided_at TIMESTAMPTZ NOT NULL,
  CHECK (decision IN ('accepted', 'modified', 'rejected')),
  UNIQUE (experiment_id, suggestion_execution_key),
  UNIQUE (project_id, actual_execution_key)
);

CREATE INDEX IF NOT EXISTS idx_research_shadow_recommendations_project
  ON research_shadow_recommendations(project_id, decided_at DESC);
