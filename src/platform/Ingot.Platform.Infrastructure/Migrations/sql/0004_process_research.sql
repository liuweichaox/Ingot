CREATE TABLE IF NOT EXISTS process_research_projects (
  project_id UUID PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL,
  revision INTEGER NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (revision > 0),
  CHECK (status IN ('draft', 'active', 'validating', 'completed', 'archived'))
);

CREATE INDEX IF NOT EXISTS idx_process_research_projects_status
  ON process_research_projects(status, updated_at DESC);

CREATE TABLE IF NOT EXISTS research_hypotheses (
  hypothesis_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('proposed', 'selected', 'supported', 'rejected', 'inconclusive'))
);

CREATE INDEX IF NOT EXISTS idx_research_hypotheses_project
  ON research_hypotheses(project_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS research_experiments (
  experiment_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('planned', 'approved', 'running', 'completed', 'cancelled'))
);

CREATE INDEX IF NOT EXISTS idx_research_experiments_project
  ON research_experiments(project_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS research_operating_regions (
  operating_region_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('candidate', 'validated', 'superseded'))
);

CREATE INDEX IF NOT EXISTS idx_research_operating_regions_project
  ON research_operating_regions(project_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS research_knowledge_claims (
  claim_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('draft', 'reviewed', 'published', 'retired'))
);

CREATE INDEX IF NOT EXISTS idx_research_knowledge_claims_project
  ON research_knowledge_claims(project_id, updated_at DESC);
