CREATE TABLE IF NOT EXISTS research_experiment_runs (
  experiment_id UUID NOT NULL REFERENCES research_experiments(experiment_id) ON DELETE CASCADE,
  execution_key TEXT NOT NULL,
  sequence INTEGER NOT NULL,
  payload JSONB NOT NULL,
  PRIMARY KEY (experiment_id, execution_key),
  UNIQUE (experiment_id, sequence),
  CHECK (sequence > 0)
);

CREATE TABLE IF NOT EXISTS research_operating_region_results (
  operating_region_id UUID NOT NULL REFERENCES research_operating_regions(operating_region_id) ON DELETE CASCADE,
  result_id UUID NOT NULL REFERENCES research_experiment_results(result_id),
  PRIMARY KEY (operating_region_id, result_id)
);

CREATE INDEX IF NOT EXISTS idx_research_operating_region_results_result
  ON research_operating_region_results(result_id, operating_region_id);

CREATE TABLE IF NOT EXISTS research_evidence (
  evidence_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  resource_type TEXT NOT NULL,
  resource_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  reference_id TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  UNIQUE (resource_type, resource_id, kind, reference_id),
  CHECK (kind IN (
    'dataset-snapshot',
    'experiment-result',
    'analysis-run',
    'mechanism-model',
    'knowledge-source',
    'operating-region')),
  CHECK (content_hash ~ '^[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS idx_research_evidence_project_resource
  ON research_evidence(project_id, resource_type, resource_id, created_at DESC);
