CREATE TABLE research_transfer_assessments (
  assessment_id       UUID PRIMARY KEY,
  project_id          UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  source_project_id   UUID NOT NULL REFERENCES process_research_projects(project_id),
  source_window_id    UUID NOT NULL REFERENCES research_process_windows(window_id),
  status              TEXT NOT NULL CHECK (status IN ('recorded', 'reviewed')),
  outcome             TEXT NOT NULL CHECK (outcome IN ('beneficial', 'neutral', 'negative-transfer', 'insufficient-evidence')),
  record_hash         TEXT NOT NULL CHECK (record_hash ~ '^[a-f0-9]{64}$'),
  payload             JSONB NOT NULL,
  created_at          TIMESTAMPTZ NOT NULL,
  reviewed_at         TIMESTAMPTZ NULL,
  UNIQUE (project_id, source_window_id, record_hash)
);

CREATE INDEX ix_research_transfer_assessments_project
  ON research_transfer_assessments(project_id, created_at DESC, assessment_id);

CREATE INDEX ix_research_transfer_assessments_source
  ON research_transfer_assessments(source_project_id, source_window_id, created_at DESC);
