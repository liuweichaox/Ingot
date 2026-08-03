CREATE TABLE IF NOT EXISTS scenario_packages (
  package_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  data_model_id TEXT NOT NULL,
  data_model_version INTEGER NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (package_id, version),
  CHECK (version > 0),
  CHECK (data_model_version > 0)
);

CREATE INDEX IF NOT EXISTS idx_scenario_packages_model
  ON scenario_packages(data_model_id, data_model_version);
