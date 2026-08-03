CREATE TABLE research_rollback_drills (
  drill_id       UUID PRIMARY KEY,
  project_id     UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  status         TEXT NOT NULL CHECK (status IN ('recorded', 'reviewed')),
  passed         BOOLEAN NOT NULL,
  record_hash    TEXT NOT NULL CHECK (record_hash ~ '^[a-f0-9]{64}$'),
  payload        JSONB NOT NULL,
  conducted_at   TIMESTAMPTZ NOT NULL,
  recorded_at    TIMESTAMPTZ NOT NULL,
  reviewed_at    TIMESTAMPTZ NULL
);

CREATE INDEX ix_research_rollback_drills_project
  ON research_rollback_drills(project_id, recorded_at DESC, drill_id);
