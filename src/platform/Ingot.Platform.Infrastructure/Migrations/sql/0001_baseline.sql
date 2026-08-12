-- 0001_baseline.sql
-- Ingot 数据库基线：由各 Store 的启动期 DDL 于 2026-07-24 逐字提取（提取顺序 = 原 HostedService 注册顺序）。
-- 本文件之后，schema 变更只能通过新增编号迁移文件表达；不允许修改本文件。
-- 注意：TimescaleDB 扩展与 hypertable 转换/压缩/保留策略仍由
--       PostgresPlatformEventStore/PostgresTimeSeriesStore 的既有幂等逻辑负责（配置驱动）。

-- ===== manufacturing / tooling / production contexts  (来源: Manufacturing/PostgresManufacturingContextStore.cs) =====
CREATE TABLE IF NOT EXISTS tooling_types (
  tooling_type_code TEXT NOT NULL,
  version INTEGER NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (tooling_type_code, version),
  CHECK (version > 0)
);

CREATE TABLE IF NOT EXISTS tooling_component_types (
  component_type_code TEXT PRIMARY KEY,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS tooling_components (
  component_id TEXT PRIMARY KEY,
  component_type_code TEXT NOT NULL,
  serial_no TEXT NOT NULL UNIQUE,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_tooling_components_component_type
  ON tooling_components(component_type_code);

CREATE TABLE IF NOT EXISTS tooling_assemblies (
  tooling_assembly_id TEXT PRIMARY KEY,
  tooling_type_code TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS tooling_assembly_revisions (
  assembly_revision_id UUID PRIMARY KEY,
  tooling_assembly_id TEXT NOT NULL REFERENCES tooling_assemblies(tooling_assembly_id),
  revision INTEGER NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  UNIQUE (tooling_assembly_id, revision),
  CHECK (revision > 0)
);

CREATE TABLE IF NOT EXISTS tooling_installations (
  installation_id UUID PRIMARY KEY,
  equipment_id TEXT NOT NULL,
  assembly_revision_id UUID NOT NULL REFERENCES tooling_assembly_revisions(assembly_revision_id),
  installed_at TIMESTAMPTZ NOT NULL,
  removed_at TIMESTAMPTZ,
  source TEXT NOT NULL,
  command_id TEXT UNIQUE,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  CHECK (removed_at IS NULL OR removed_at > installed_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_tooling_installations_active_equipment
  ON tooling_installations(equipment_id) WHERE removed_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS idx_tooling_installations_active_revision
  ON tooling_installations(assembly_revision_id) WHERE removed_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_tooling_installations_equipment_time
  ON tooling_installations(equipment_id, installed_at, removed_at);

CREATE TABLE IF NOT EXISTS production_contexts (
  context_id UUID PRIMARY KEY,
  equipment_id TEXT NOT NULL,
  tooling_installation_id UUID NOT NULL REFERENCES tooling_installations(installation_id),
  valid_from TIMESTAMPTZ NOT NULL,
  valid_to TIMESTAMPTZ,
  source TEXT NOT NULL,
  command_id TEXT UNIQUE,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (valid_to IS NULL OR valid_to > valid_from)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_production_contexts_active_equipment
  ON production_contexts(equipment_id) WHERE valid_to IS NULL;
CREATE INDEX IF NOT EXISTS idx_production_contexts_equipment_time
  ON production_contexts(equipment_id, valid_from, valid_to);
CREATE UNIQUE INDEX IF NOT EXISTS idx_production_contexts_command_id
  ON production_contexts(command_id) WHERE command_id IS NOT NULL;

-- ===== execution analysis materializations / phases / features / backfill / phase+feature definitions  (来源: ProcessExecutions/PostgresProcessExecutionAnalysisMaterializationStore.cs) =====
CREATE TABLE IF NOT EXISTS execution_analysis_materializations (
  execution_id                   TEXT NOT NULL,
  algorithm_version                TEXT NOT NULL,
  data_model_id                    TEXT NOT NULL,
  data_model_version               INTEGER NOT NULL,
  analysis_plan_id                 TEXT NOT NULL,
  analysis_plan_version            INTEGER NOT NULL,
  source_max_ingest_id              BIGINT NOT NULL,
  source_event_count                INTEGER NOT NULL,
  status                            TEXT NOT NULL,
  computed_at                       TIMESTAMPTZ NOT NULL,
  invalidated_at                    TIMESTAMPTZ,
  invalidated_source_max_ingest_id  BIGINT NOT NULL DEFAULT 0,
  invalidation_reason               TEXT,
  result                            JSONB NOT NULL,
  PRIMARY KEY (
    execution_id, algorithm_version,
    data_model_id, data_model_version,
    analysis_plan_id, analysis_plan_version)
);

CREATE INDEX IF NOT EXISTS idx_execution_analysis_materializations_status
  ON execution_analysis_materializations (status, computed_at);

CREATE TABLE IF NOT EXISTS execution_phases (
  execution_id          TEXT NOT NULL,
  algorithm_version       TEXT NOT NULL,
  data_model_id           TEXT NOT NULL,
  data_model_version      INTEGER NOT NULL,
  analysis_plan_id        TEXT NOT NULL,
  analysis_plan_version   INTEGER NOT NULL,
  phase_code              TEXT NOT NULL,
  phase_name              TEXT NOT NULL,
  phase_order             INTEGER NOT NULL,
  phase_source            TEXT NOT NULL,
  required                BOOLEAN NOT NULL,
  is_complete             BOOLEAN NOT NULL,
  sample_count            INTEGER NOT NULL,
  started_at              TIMESTAMPTZ,
  ended_at                TIMESTAMPTZ,
  PRIMARY KEY (
    execution_id, algorithm_version,
    data_model_id, data_model_version,
    analysis_plan_id, analysis_plan_version,
    phase_order)
);

CREATE INDEX IF NOT EXISTS idx_execution_phases_code_time
  ON execution_phases (phase_code, started_at);

CREATE TABLE IF NOT EXISTS execution_features (
  execution_id          TEXT NOT NULL,
  algorithm_version       TEXT NOT NULL,
  data_model_id           TEXT NOT NULL,
  data_model_version      INTEGER NOT NULL,
  analysis_plan_id        TEXT NOT NULL,
  analysis_plan_version   INTEGER NOT NULL,
  signal_code             TEXT NOT NULL,
  signal_name             TEXT NOT NULL,
  signal_unit             TEXT,
  signal_sample_count     INTEGER NOT NULL,
  phase_code              TEXT NOT NULL,
  phase_name              TEXT,
  phase_order             INTEGER NOT NULL,
  phase_source            TEXT NOT NULL,
  feature_code            TEXT NOT NULL,
  feature_definition_version INTEGER NOT NULL DEFAULT 1,
  feature_definition_hash TEXT NOT NULL DEFAULT '',
  computation_hash        TEXT NOT NULL DEFAULT '',
  input_point_count       INTEGER NOT NULL DEFAULT 0,
  feature_value           DOUBLE PRECISION,
  valid_duration_ms       DOUBLE PRECISION NOT NULL,
  coverage                DOUBLE PRECISION NOT NULL,
  started_at              TIMESTAMPTZ,
  ended_at                TIMESTAMPTZ,
  PRIMARY KEY (
    execution_id, algorithm_version,
    data_model_id, data_model_version,
    analysis_plan_id, analysis_plan_version,
    signal_code, phase_code, phase_order, feature_code)
);

CREATE INDEX IF NOT EXISTS idx_execution_features_lookup
  ON execution_features (signal_code, phase_code, feature_code, execution_id);

CREATE TABLE IF NOT EXISTS execution_analysis_backfill_jobs (
  job_id UUID PRIMARY KEY,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('queued', 'running', 'completed', 'completed_with_errors', 'failed'))
);
CREATE INDEX IF NOT EXISTS idx_execution_analysis_backfill_jobs_status
  ON execution_analysis_backfill_jobs(status, created_at);

-- ===== time-series samples / signal definitions / collection points  (来源: TimeSeries/PostgresTimeSeriesStore.cs) =====
CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS signal_definitions (
  data_model_id      TEXT NOT NULL,
  data_model_version INTEGER NOT NULL,
  signal_code        TEXT NOT NULL,
  source_field       TEXT NOT NULL,
  data_type          TEXT NOT NULL,
  unit               TEXT,
  category           TEXT NOT NULL,
  definition_hash    TEXT NOT NULL,
  first_seen_at      TIMESTAMPTZ NOT NULL,
  last_seen_at       TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (data_model_id, data_model_version, signal_code)
);

CREATE TABLE IF NOT EXISTS collection_points (
  collection_point_id TEXT PRIMARY KEY,
  edge_id              TEXT NOT NULL,
  subject_type         TEXT NOT NULL,
  subject_id           TEXT NOT NULL,
  signal_code          TEXT NOT NULL,
  static_tags          JSONB NOT NULL DEFAULT '{}'::jsonb,
  first_seen_at        TIMESTAMPTZ NOT NULL,
  last_seen_at         TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS time_series_samples (
  occurred_at          TIMESTAMPTZ NOT NULL,
  collection_point_id  TEXT NOT NULL,
  signal_code          TEXT NOT NULL,
  data_type            TEXT NOT NULL,
  unit                 TEXT,
  category             TEXT NOT NULL,
  numeric_value        DOUBLE PRECISION,
  integer_value        BIGINT,
  boolean_value        BOOLEAN,
  text_value           TEXT,
  quality_code         TEXT NOT NULL,
  event_id             TEXT NOT NULL,
  ingest_id            BIGINT NOT NULL,
  recorded_at          TIMESTAMPTZ NOT NULL,
  edge_id              TEXT NOT NULL,
  source               TEXT NOT NULL,
  subject_type         TEXT NOT NULL,
  subject_id           TEXT NOT NULL,
  execution_id       TEXT,
  phase_code           TEXT,
  data_model_id        TEXT NOT NULL,
  data_model_version   INTEGER NOT NULL,
  run_context          JSONB NOT NULL DEFAULT '{}'::jsonb,
  CONSTRAINT ck_time_series_samples_one_value CHECK (
    num_nonnulls(numeric_value, integer_value, boolean_value, text_value) = 1
  ),
  CONSTRAINT ck_time_series_samples_quality CHECK (
    quality_code IN ('good', 'uncertain', 'bad')
  )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_time_series_samples_source
  ON time_series_samples (event_id, signal_code, occurred_at);
CREATE INDEX IF NOT EXISTS ix_time_series_samples_point_time
  ON time_series_samples (collection_point_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_time_series_samples_signal_time
  ON time_series_samples (signal_code, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_time_series_samples_correlation
  ON time_series_samples (execution_id, signal_code, occurred_at);
CREATE INDEX IF NOT EXISTS ix_time_series_samples_context
  ON time_series_samples USING GIN (run_context);

-- ===== production events / ingest keys / context snapshots  (来源: Events/PostgresPlatformEventStore.cs) =====
CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE SEQUENCE IF NOT EXISTS production_events_ingest_id_seq;

CREATE TABLE IF NOT EXISTS event_ingest_keys (
  event_id    TEXT PRIMARY KEY,
  edge_id     TEXT NOT NULL,
  seq         BIGINT NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL,
  UNIQUE (edge_id, seq)
);

CREATE TABLE IF NOT EXISTS production_events (
  ingest_id      BIGINT NOT NULL DEFAULT nextval('production_events_ingest_id_seq'),
  event_id       TEXT NOT NULL,
  edge_id        TEXT NOT NULL,
  seq            BIGINT NOT NULL,
  event_type     TEXT NOT NULL,
  type_version   INTEGER NOT NULL,
  occurred_at    TIMESTAMPTZ NOT NULL,
  recorded_at    TIMESTAMPTZ NOT NULL,
  ingested_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  source         TEXT NOT NULL,
  subject_type   TEXT NOT NULL,
  subject_id     TEXT NOT NULL,
  execution_id TEXT,
  context        JSONB NOT NULL DEFAULT '{}'::jsonb,
  data           JSONB NOT NULL DEFAULT '{}'::jsonb
);

CREATE TABLE IF NOT EXISTS operation_context_snapshots (
  execution_id    TEXT PRIMARY KEY,
  subject_type      TEXT NOT NULL,
  subject_id        TEXT NOT NULL,
  started_event_type TEXT NOT NULL,
  captured_at       TIMESTAMPTZ NOT NULL,
  context           JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_production_events_ingest
  ON production_events (ingest_id);
CREATE INDEX IF NOT EXISTS idx_production_events_type_time
  ON production_events (event_type, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_production_events_subject_time
  ON production_events (subject_type, subject_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_production_events_correlation
  ON production_events (execution_id, occurred_at);
CREATE INDEX IF NOT EXISTS idx_production_events_context
  ON production_events USING GIN (context);

-- ===== inspection definitions / plans / scopes  (来源: Inspections/PostgresInspectionMasterDataStore.cs) =====
CREATE TABLE IF NOT EXISTS inspection_definitions (
  code TEXT NOT NULL,
  version INTEGER NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (code, version),
  CHECK (version > 0)
);

CREATE TABLE IF NOT EXISTS inspection_plans (
  plan_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (plan_id, version),
  CHECK (version > 0)
);

CREATE TABLE IF NOT EXISTS phase_definitions (
  code TEXT PRIMARY KEY,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS phase_mappings (
  mapping_id TEXT PRIMARY KEY,
  process_specification_id TEXT NOT NULL,
  process_specification_version TEXT,
  process_template TEXT,
  process_step TEXT NOT NULL,
  phase_code TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_phase_mappings_lookup
  ON phase_mappings(process_specification_id, process_specification_version, process_template, process_step);

CREATE TABLE IF NOT EXISTS feature_definitions (
  code TEXT PRIMARY KEY,
  phase_code TEXT NOT NULL,
  signal TEXT NOT NULL,
  aggregation TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_feature_definitions_phase
  ON feature_definitions(phase_code);

-- ===== inspection records  (来源: Inspections/PostgresInspectionRecordStore.cs) =====
CREATE TABLE IF NOT EXISTS inspection_records (
  record_id           UUID PRIMARY KEY,
  output_item_id        TEXT NOT NULL,
  execution_id    TEXT NOT NULL,
  definition_code     TEXT NOT NULL,
  definition_version  INTEGER NOT NULL,
  measured_at         TIMESTAMPTZ NOT NULL,
  recorded_at         TIMESTAMPTZ NOT NULL,
  ingested_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
  outcome             TEXT NOT NULL,
  submitted_by        TEXT NOT NULL,
  submitter_verified  BOOLEAN NOT NULL,
  instrument          JSONB,
  measurements        JSONB NOT NULL DEFAULT '[]'::jsonb,
  attachments            JSONB NOT NULL DEFAULT '[]'::jsonb,
  notes               TEXT,
  supersedes_record_id UUID,
  correction_reason   TEXT,
  payload_hash        TEXT NOT NULL,
  CHECK (definition_version > 0),
  CHECK (outcome IN ('PASS', 'FAIL', 'INCONCLUSIVE'))
);
CREATE INDEX IF NOT EXISTS idx_inspection_records_output_item_time
  ON inspection_records(output_item_id, measured_at DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_records_execution_time
  ON inspection_records(execution_id, measured_at DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_records_definition_time
  ON inspection_records(definition_code, measured_at DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_records_outcome_time
  ON inspection_records(outcome, measured_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS idx_inspection_records_one_correction
  ON inspection_records(supersedes_record_id) WHERE supersedes_record_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS inspection_scopes (
  scope_id TEXT PRIMARY KEY,
  scope_type TEXT NOT NULL,
  subject_id TEXT NOT NULL,
  from_at TIMESTAMPTZ NOT NULL,
  to_at TIMESTAMPTZ NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (scope_type IN ('analysis-window', 'production-run', 'material-lot')),
  CHECK (to_at > from_at)
);
CREATE INDEX IF NOT EXISTS idx_inspection_scopes_subject_time
  ON inspection_scopes(subject_id, to_at DESC);

-- ===== inspection attachments  (来源: Inspections/PostgresInspectionAttachmentStore.cs) =====
CREATE TABLE IF NOT EXISTS inspection_attachments (
  attachment_id UUID PRIMARY KEY,
  storage_ref TEXT NOT NULL,
  sha256 TEXT NOT NULL UNIQUE,
  media_type TEXT NOT NULL,
  file_name TEXT NOT NULL,
  size_bytes BIGINT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CHECK (size_bytes > 0)
);
CREATE INDEX IF NOT EXISTS idx_inspection_attachments_sha256
  ON inspection_attachments(sha256);

-- ===== inspection reviews / audit log  (来源: Inspections/PostgresInspectionReviewStore.cs) =====
CREATE TABLE IF NOT EXISTS inspection_reviews (
  review_id            UUID PRIMARY KEY,
  inspection_record_id UUID NOT NULL,
  execution_id     TEXT NOT NULL,
  decision             TEXT NOT NULL,
  reviewed_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  reviewed_by          TEXT NOT NULL,
  notes                TEXT,
  payload_hash         TEXT NOT NULL,
  CHECK (decision IN ('CONFIRMED', 'REJECTED', 'REINSPECTION_REQUIRED'))
);
CREATE INDEX IF NOT EXISTS idx_inspection_reviews_record_time
  ON inspection_reviews(inspection_record_id, reviewed_at DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_reviews_operation_time
  ON inspection_reviews(execution_id, reviewed_at DESC);

CREATE TABLE IF NOT EXISTS inspection_audit_log (
  audit_id             BIGSERIAL PRIMARY KEY,
  inspection_record_id UUID,
  attachment_id        UUID,
  action               TEXT NOT NULL,
  occurred_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  actor                TEXT NOT NULL,
  detail               TEXT
);
CREATE INDEX IF NOT EXISTS idx_inspection_audit_record_time
  ON inspection_audit_log(inspection_record_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_audit_attachment_time
  ON inspection_audit_log(attachment_id, occurred_at DESC);

-- ===== process data models / processSpecifications / analysis plans  (来源: ProcessConfiguration/PostgresProcessConfigurationStore.cs) =====
CREATE TABLE IF NOT EXISTS process_data_models (
  model_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (model_id, version),
  CHECK (version > 0)
);

CREATE TABLE IF NOT EXISTS process_specification_versions (
  process_specification_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  data_model_id TEXT NOT NULL,
  data_model_version INTEGER NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (process_specification_id, version),
  CHECK (version > 0),
  CHECK (data_model_version > 0)
);
CREATE INDEX IF NOT EXISTS idx_process_specification_versions_model
  ON process_specification_versions(data_model_id, data_model_version);

CREATE TABLE IF NOT EXISTS process_analysis_plans (
  plan_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  data_model_id TEXT NOT NULL,
  data_model_version INTEGER NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (plan_id, version),
  CHECK (version > 0),
  CHECK (data_model_version > 0)
);
CREATE INDEX IF NOT EXISTS idx_process_analysis_plans_model
  ON process_analysis_plans(data_model_id, data_model_version);

-- ===== process improvement chain  (来源: ProcessImprovement/PostgresProcessImprovementStore.cs) =====
CREATE TABLE IF NOT EXISTS training_dataset_versions (
  dataset_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (dataset_id, version),
  CHECK (version > 0)
);

CREATE TABLE IF NOT EXISTS process_model_versions (
  model_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  dataset_id TEXT NOT NULL,
  dataset_version INTEGER NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (model_id, version),
  FOREIGN KEY (dataset_id, dataset_version)
    REFERENCES training_dataset_versions(dataset_id, version),
  CHECK (version > 0),
  CHECK (status IN ('draft', 'validated', 'active', 'suspended', 'retired'))
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_process_model_active
  ON process_model_versions(model_id) WHERE status = 'active';

CREATE TABLE IF NOT EXISTS model_evaluations (
  evaluation_id UUID PRIMARY KEY,
  model_id TEXT NOT NULL,
  model_version INTEGER NOT NULL,
  passed BOOLEAN NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  FOREIGN KEY (model_id, model_version)
    REFERENCES process_model_versions(model_id, version)
);

CREATE TABLE IF NOT EXISTS model_drift_readings (
  reading_id UUID PRIMARY KEY,
  model_id TEXT NOT NULL,
  model_version INTEGER NOT NULL,
  value DOUBLE PRECISION NOT NULL,
  stop_threshold DOUBLE PRECISION NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  FOREIGN KEY (model_id, model_version)
    REFERENCES process_model_versions(model_id, version)
);
CREATE INDEX IF NOT EXISTS idx_model_drift_readings_model
  ON model_drift_readings(model_id, model_version, created_at DESC);

CREATE TABLE IF NOT EXISTS mechanism_model_versions (
  model_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (model_id, version),
  CHECK (version > 0),
  CHECK (status IN ('draft', 'validated', 'active', 'retired'))
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_mechanism_model_active
  ON mechanism_model_versions(model_id) WHERE status = 'active';

CREATE TABLE IF NOT EXISTS mechanism_fusion_definitions (
  fusion_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  mode TEXT NOT NULL,
  mechanism_model_id TEXT NOT NULL,
  mechanism_model_version INTEGER NOT NULL,
  content_hash TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (fusion_id, version),
  FOREIGN KEY (mechanism_model_id, mechanism_model_version)
    REFERENCES mechanism_model_versions(model_id, version),
  CHECK (version > 0),
  CHECK (status IN ('draft', 'validated', 'active', 'retired')),
  CHECK (mode IN ('calibration', 'post-processing', 'mechanism-as-feature', 'ensemble'))
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_mechanism_fusion_active
  ON mechanism_fusion_definitions(fusion_id) WHERE status = 'active';

CREATE TABLE IF NOT EXISTS dataset_quality_validation_reports (
  report_id UUID PRIMARY KEY,
  dataset_id TEXT NOT NULL,
  dataset_version INTEGER NOT NULL,
  industry TEXT NOT NULL,
  status TEXT NOT NULL,
  source_sha256 TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  CHECK (dataset_version > 0),
  CHECK (status IN ('passed', 'rejected'))
);
CREATE INDEX IF NOT EXISTS idx_dataset_quality_validation_dataset
  ON dataset_quality_validation_reports(dataset_id, dataset_version, created_at DESC);

CREATE TABLE IF NOT EXISTS process_knowledge_sources (
  source_id UUID PRIMARY KEY,
  status TEXT NOT NULL,
  storage_ref TEXT NOT NULL,
  sha256 TEXT NOT NULL UNIQUE,
  file_name TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('uploaded', 'indexed', 'reviewed', 'retired'))
);

CREATE TABLE IF NOT EXISTS process_knowledge_records (
  record_id UUID PRIMARY KEY,
  source_id UUID NOT NULL REFERENCES process_knowledge_sources(source_id),
  human_reviewed BOOLEAN NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_process_knowledge_records_source
  ON process_knowledge_records(source_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS research_asset_audit (
  entry_id UUID PRIMARY KEY,
  resource_type TEXT NOT NULL,
  resource_id TEXT NOT NULL,
  action TEXT NOT NULL,
  payload JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_research_asset_audit_resource
  ON research_asset_audit(resource_type, resource_id, created_at);

-- ===== ingestion configuration (来源: Acquisition stores) =====
CREATE TABLE IF NOT EXISTS ingestion_task_templates (
  template_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  status TEXT NOT NULL,
  protocol TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (template_id, version),
  CHECK (version > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ingestion_task_templates_published
  ON ingestion_task_templates(template_id) WHERE status = 'published';

CREATE TABLE IF NOT EXISTS data_source_instances (
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
CREATE INDEX IF NOT EXISTS idx_data_source_instances_edge_status
  ON data_source_instances(edge_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS uq_data_source_instances_published
  ON data_source_instances(data_source_id) WHERE status = 'published';

CREATE TABLE IF NOT EXISTS ingestion_task_bindings (
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
CREATE UNIQUE INDEX IF NOT EXISTS uq_ingestion_task_bindings_published
  ON ingestion_task_bindings(task_id) WHERE status = 'published';

CREATE TABLE IF NOT EXISTS ingestion_tasks (
  task_id TEXT NOT NULL,
  version INTEGER NOT NULL,
  edge_id TEXT NOT NULL,
  status TEXT NOT NULL,
  payload JSONB NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (task_id, version),
  CHECK (version > 0)
);
CREATE INDEX IF NOT EXISTS idx_ingestion_tasks_edge_status
  ON ingestion_tasks(edge_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ingestion_tasks_published
  ON ingestion_tasks(task_id) WHERE status = 'published';

-- ===== webhook subscriptions  (来源: Webhooks/PostgresWebhookSubscriptionStore.cs) =====
CREATE TABLE IF NOT EXISTS webhook_subscriptions (
  subscription_id      UUID PRIMARY KEY,
  name                 TEXT NOT NULL,
  endpoint             TEXT NOT NULL,
  event_types          JSONB NOT NULL DEFAULT '[]'::jsonb,
  subject_type         TEXT,
  subject_id           TEXT,
  context_filter       JSONB NOT NULL DEFAULT '{}'::jsonb,
  secret               TEXT,
  cursor               BIGINT NOT NULL DEFAULT 0,
  enabled              BOOLEAN NOT NULL DEFAULT TRUE,
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_success_at      TIMESTAMPTZ,
  last_error           TEXT,
  consecutive_failures INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_webhook_subscriptions_enabled
  ON webhook_subscriptions(enabled, created_at);
