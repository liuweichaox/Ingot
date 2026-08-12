-- Introduce the versioned ingestion configuration model after persisted profiles
-- have been translated to the current task contract by migration 0021.

CREATE TABLE ingestion_task_templates (
  template_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  protocol TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (template_id, version),
  CHECK (version > 0)
);
CREATE UNIQUE INDEX uq_ingestion_task_templates_published
  ON ingestion_task_templates(template_id) WHERE status = 'published';

CREATE TABLE data_source_instances (
  data_source_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  edge_id TEXT NOT NULL,
  status TEXT NOT NULL,
  protocol TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (data_source_id, version),
  CHECK (version > 0)
);
CREATE INDEX idx_data_source_instances_edge_status
  ON data_source_instances(edge_id, status);
CREATE UNIQUE INDEX uq_data_source_instances_published
  ON data_source_instances(data_source_id) WHERE status = 'published';

CREATE TABLE ingestion_task_bindings (
  task_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  template_id TEXT NOT NULL,
  template_version INTEGER NOT NULL,
  data_source_id TEXT NOT NULL,
  data_source_version INTEGER NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (task_id, version),
  CHECK (version > 0),
  CHECK (template_version > 0),
  CHECK (data_source_version > 0),
  FOREIGN KEY (template_id, template_version)
    REFERENCES ingestion_task_templates(template_id, version),
  FOREIGN KEY (data_source_id, data_source_version)
    REFERENCES data_source_instances(data_source_id, version)
);
CREATE UNIQUE INDEX uq_ingestion_task_bindings_published
  ON ingestion_task_bindings(task_id) WHERE status = 'published';

CREATE TABLE ingestion_tasks (
  task_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  edge_id TEXT NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (task_id, version),
  CHECK (version > 0)
);

INSERT INTO ingestion_tasks(task_id, version, edge_id, status, payload, updated_at)
SELECT
  profile_id,
  version,
  edge_id,
  status,
  payload,
  updated_at
FROM acquisition_profiles;

-- A historical concurrent publication could have left more than one published version.
-- Keep the newest version active and make the migrated invariant explicit before adding the index.
WITH ranked AS (
  SELECT
    task_id,
    version,
    row_number() OVER (PARTITION BY task_id ORDER BY version DESC) AS ordinal
  FROM ingestion_tasks
  WHERE status = 'published'
)
UPDATE ingestion_tasks AS target
SET
  status = 'retired',
  payload = jsonb_set(target.payload, '{status}', '"retired"'::jsonb),
  updated_at = now()
FROM ranked
WHERE target.task_id = ranked.task_id
  AND target.version = ranked.version
  AND ranked.ordinal > 1;

CREATE INDEX idx_ingestion_tasks_edge_status
  ON ingestion_tasks(edge_id, status);
CREATE UNIQUE INDEX uq_ingestion_tasks_published
  ON ingestion_tasks(task_id) WHERE status = 'published';

DROP TABLE acquisition_profiles;
