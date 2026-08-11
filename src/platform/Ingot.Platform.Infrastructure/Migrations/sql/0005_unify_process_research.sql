CREATE TABLE IF NOT EXISTS research_project_members (
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  user_id TEXT NOT NULL,
  PRIMARY KEY (project_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_research_project_members_user
  ON research_project_members(user_id, project_id);

CREATE TABLE IF NOT EXISTS research_experiment_results (
  result_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  experiment_id UUID NOT NULL REFERENCES research_experiments(experiment_id),
  analysis_run_id UUID NOT NULL,
  analysis_hash TEXT NOT NULL,
  safety_passed BOOLEAN NOT NULL,
  payload JSONB NOT NULL,
  recorded_at TIMESTAMPTZ NOT NULL,
  CHECK (analysis_hash ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS idx_research_experiment_results_project
  ON research_experiment_results(project_id, recorded_at DESC);

CREATE INDEX IF NOT EXISTS idx_research_experiment_results_experiment
  ON research_experiment_results(experiment_id, recorded_at DESC);

CREATE TABLE IF NOT EXISTS process_research_audit (
  entry_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  resource_type TEXT NOT NULL,
  resource_id TEXT NOT NULL,
  action TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_process_research_audit_project
  ON process_research_audit(project_id, created_at DESC);
