CREATE TABLE agent_runs (
  run_id       TEXT PRIMARY KEY,
  user_id      TEXT NOT NULL,
  entry_point  TEXT NOT NULL,
  status       TEXT NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL,
  completed_at TIMESTAMPTZ NULL,
  snapshot     JSONB NOT NULL,
  updated_at   TIMESTAMPTZ NOT NULL
);

CREATE INDEX ix_agent_runs_user_entry_created
  ON agent_runs(user_id, entry_point, created_at DESC, run_id);

CREATE TABLE agent_stream_events (
  sequence    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  run_id      TEXT NOT NULL REFERENCES agent_runs(run_id) ON DELETE CASCADE,
  event_type  TEXT NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL,
  data        JSONB NULL
);

CREATE INDEX ix_agent_stream_events_run_sequence
  ON agent_stream_events(run_id, sequence);

ALTER TABLE golden_question_evaluations
  ADD COLUMN agent_run_snapshot JSONB NULL,
  ADD COLUMN agent_run_snapshot_hash TEXT NULL
    CHECK (agent_run_snapshot_hash IS NULL OR agent_run_snapshot_hash ~ '^[a-f0-9]{64}$'),
  ADD CONSTRAINT fk_golden_question_evaluation_agent_run
    FOREIGN KEY (agent_run_id) REFERENCES agent_runs(run_id) ON DELETE RESTRICT NOT VALID;

ALTER TABLE golden_question_evaluations
  ADD CONSTRAINT ck_golden_question_evaluation_snapshot_pair
  CHECK (
    (agent_run_snapshot IS NULL AND agent_run_snapshot_hash IS NULL) OR
    (agent_run_snapshot IS NOT NULL AND agent_run_snapshot_hash IS NOT NULL)
  );

CREATE INDEX ix_research_experiments_project_page
  ON research_experiments(project_id, updated_at DESC, experiment_id DESC);
CREATE INDEX ix_research_experiment_results_project_page
  ON research_experiment_results(project_id, recorded_at DESC, result_id DESC);
CREATE INDEX ix_research_shadow_recommendations_project_page
  ON research_shadow_recommendations(project_id, decided_at DESC, recommendation_id DESC);
CREATE INDEX ix_research_historical_replay_reports_project_page
  ON research_historical_replay_reports(project_id, generated_at DESC, report_id DESC);
CREATE INDEX ix_process_research_audit_project_page
  ON process_research_audit(project_id, created_at DESC, entry_id DESC);
