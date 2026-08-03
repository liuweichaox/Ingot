CREATE TABLE IF NOT EXISTS research_historical_replay_reports (
  report_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id),
  status TEXT NOT NULL,
  dataset_snapshot_hash TEXT NOT NULL,
  report_hash TEXT NOT NULL,
  payload JSONB NOT NULL,
  generated_at TIMESTAMPTZ NOT NULL,
  reviewed_at TIMESTAMPTZ,
  CHECK (status IN ('generated', 'reviewed')),
  CHECK (dataset_snapshot_hash ~ '^[a-f0-9]{64}$'),
  CHECK (report_hash ~ '^[a-f0-9]{64}$'),
  UNIQUE (project_id, dataset_snapshot_hash, report_hash)
);

CREATE INDEX IF NOT EXISTS idx_research_historical_replay_reports_project
  ON research_historical_replay_reports(project_id, generated_at DESC);
