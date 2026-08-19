CREATE TABLE research_validation_preregistrations (
  preregistration_id    UUID PRIMARY KEY,
  project_id            UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  version               INTEGER NOT NULL CHECK (version > 0),
  project_revision      INTEGER NOT NULL CHECK (project_revision > 0),
  status                TEXT NOT NULL CHECK (status IN ('frozen', 'reviewed')),
  project_snapshot_hash TEXT NOT NULL CHECK (project_snapshot_hash ~ '^[a-f0-9]{64}$'),
  content_hash          TEXT NOT NULL CHECK (content_hash ~ '^[a-f0-9]{64}$'),
  payload               JSONB NOT NULL,
  frozen_at             TIMESTAMPTZ NOT NULL,
  reviewed_at           TIMESTAMPTZ NULL,
  UNIQUE (project_id, version),
  UNIQUE (project_id, content_hash)
);

CREATE INDEX ix_research_validation_preregistrations_project
  ON research_validation_preregistrations(project_id, version DESC);
