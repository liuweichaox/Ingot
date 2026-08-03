CREATE TABLE IF NOT EXISTS golden_question_cases (
  case_id UUID NOT NULL,
  version INTEGER NOT NULL CHECK (version > 0),
  status TEXT NOT NULL CHECK (status IN ('draft', 'reviewed', 'retired')),
  question TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (case_id, version)
);

CREATE INDEX IF NOT EXISTS idx_golden_question_cases_status_updated
  ON golden_question_cases(status, updated_at DESC);

CREATE TABLE IF NOT EXISTS golden_question_evaluations (
  evaluation_id UUID PRIMARY KEY,
  case_id UUID NOT NULL,
  case_version INTEGER NOT NULL,
  agent_run_id TEXT NOT NULL,
  passed BOOLEAN NOT NULL,
  payload JSONB NOT NULL,
  evaluated_at TIMESTAMPTZ NOT NULL,
  FOREIGN KEY (case_id, case_version)
    REFERENCES golden_question_cases(case_id, version) ON DELETE RESTRICT,
  UNIQUE (case_id, case_version, agent_run_id)
);

CREATE INDEX IF NOT EXISTS idx_golden_question_evaluations_case_time
  ON golden_question_evaluations(case_id, case_version, evaluated_at DESC);
