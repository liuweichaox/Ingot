-- Relational mechanism-knowledge workbench. Core knowledge fields are not stored in JSONB.

ALTER TABLE process_knowledge_sources RENAME TO knowledge_sources;
ALTER TABLE knowledge_sources
  ADD COLUMN project_id UUID,
  ADD COLUMN title TEXT,
  ADD COLUMN source_kind TEXT,
  ADD COLUMN media_type TEXT,
  ADD COLUMN size_bytes BIGINT,
  ADD COLUMN extraction_status TEXT,
  ADD COLUMN extraction_error TEXT,
  ADD COLUMN extractor_version TEXT,
  ADD COLUMN uploaded_by TEXT,
  ADD COLUMN uploaded_at TIMESTAMPTZ,
  ADD COLUMN reviewed_by TEXT,
  ADD COLUMN reviewed_at TIMESTAMPTZ;

UPDATE knowledge_sources SET
  project_id = NULLIF(payload->'contextSelector'->>'research-project-id', '')::UUID,
  title = payload->>'title',
  source_kind = payload->>'sourceKind',
  media_type = payload->>'mediaType',
  size_bytes = (payload->>'sizeBytes')::BIGINT,
  extraction_status = payload->>'extractionStatus',
  extraction_error = payload->>'extractionError',
  extractor_version = payload->>'extractorVersion',
  uploaded_by = payload->>'uploadedBy',
  uploaded_at = (payload->>'uploadedAt')::TIMESTAMPTZ,
  reviewed_by = payload->>'reviewedBy',
  reviewed_at = NULLIF(payload->>'reviewedAt', '')::TIMESTAMPTZ;

DO $migration$
BEGIN
  IF EXISTS (SELECT 1 FROM knowledge_sources WHERE project_id IS NULL) THEN
    RAISE EXCEPTION 'Knowledge source migration requires research-project-id on every source.';
  END IF;
END
$migration$;

CREATE TABLE knowledge_source_context (
  source_id UUID NOT NULL REFERENCES knowledge_sources(source_id) ON DELETE CASCADE,
  dimension_code TEXT NOT NULL,
  dimension_value TEXT NOT NULL,
  PRIMARY KEY(source_id, dimension_code)
);
INSERT INTO knowledge_source_context(source_id, dimension_code, dimension_value)
SELECT source.source_id, item.key, item.value
FROM knowledge_sources source
CROSS JOIN LATERAL jsonb_each_text(source.payload->'contextSelector') item;

ALTER TABLE knowledge_sources
  ALTER COLUMN project_id SET NOT NULL,
  ALTER COLUMN title SET NOT NULL,
  ALTER COLUMN source_kind SET NOT NULL,
  ALTER COLUMN media_type SET NOT NULL,
  ALTER COLUMN size_bytes SET NOT NULL,
  ALTER COLUMN extraction_status SET NOT NULL,
  ALTER COLUMN uploaded_by SET NOT NULL,
  ALTER COLUMN uploaded_at SET NOT NULL,
  ADD CONSTRAINT fk_knowledge_sources_project
    FOREIGN KEY(project_id) REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  DROP COLUMN payload;
CREATE INDEX ix_knowledge_sources_project ON knowledge_sources(project_id, updated_at DESC);

ALTER TABLE process_knowledge_records RENAME TO knowledge_fragments;
ALTER TABLE knowledge_fragments
  ADD COLUMN category TEXT,
  ADD COLUMN page_or_sheet TEXT,
  ADD COLUMN region TEXT,
  ADD COLUMN content TEXT,
  ADD COLUMN created_by TEXT,
  ADD COLUMN created_at TIMESTAMPTZ,
  ADD COLUMN reviewed_by TEXT,
  ADD COLUMN reviewed_at TIMESTAMPTZ,
  ADD COLUMN extraction_method TEXT,
  ADD COLUMN extractor_version TEXT,
  ADD COLUMN extraction_confidence DOUBLE PRECISION,
  ADD COLUMN location_kind TEXT,
  ADD COLUMN page_number INTEGER,
  ADD COLUMN sheet_name TEXT,
  ADD COLUMN cell_range TEXT,
  ADD COLUMN citation_region TEXT,
  ADD COLUMN content_hash TEXT;

UPDATE knowledge_fragments SET
  category = payload->>'category',
  page_or_sheet = payload->>'pageOrSheet',
  region = payload->>'region',
  content = payload->>'content',
  created_by = payload->>'createdBy',
  created_at = (payload->>'createdAt')::TIMESTAMPTZ,
  reviewed_by = payload->>'reviewedBy',
  reviewed_at = NULLIF(payload->>'reviewedAt', '')::TIMESTAMPTZ,
  extraction_method = payload->>'extractionMethod',
  extractor_version = payload->>'extractorVersion',
  extraction_confidence = NULLIF(payload->>'extractionConfidence', '')::DOUBLE PRECISION,
  location_kind = payload->'citation'->>'locationKind',
  page_number = NULLIF(payload->'citation'->>'pageNumber', '')::INTEGER,
  sheet_name = payload->'citation'->>'sheetName',
  cell_range = payload->'citation'->>'cellRange',
  citation_region = payload->'citation'->>'region',
  content_hash = payload->'citation'->>'contentHash';

CREATE TABLE knowledge_fragment_values (
  fragment_id UUID NOT NULL REFERENCES knowledge_fragments(record_id) ON DELETE CASCADE,
  value_code TEXT NOT NULL,
  value_text TEXT NOT NULL,
  PRIMARY KEY(fragment_id, value_code)
);
INSERT INTO knowledge_fragment_values(fragment_id, value_code, value_text)
SELECT fragment.record_id, item.key, item.value
FROM knowledge_fragments fragment
CROSS JOIN LATERAL jsonb_each_text(fragment.payload->'structuredValues') item;

ALTER TABLE knowledge_fragments
  ALTER COLUMN category SET NOT NULL,
  ALTER COLUMN content SET NOT NULL,
  ALTER COLUMN created_by SET NOT NULL,
  ALTER COLUMN created_at SET NOT NULL,
  ALTER COLUMN extraction_method SET NOT NULL,
  ALTER COLUMN extractor_version SET NOT NULL,
  DROP COLUMN payload;
ALTER INDEX idx_process_knowledge_records_source RENAME TO ix_knowledge_fragments_source;

CREATE TABLE mechanism_claims (
  claim_id        UUID PRIMARY KEY,
  project_id      UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  current_version INTEGER NOT NULL,
  status          TEXT NOT NULL,
  created_at      TIMESTAMPTZ NOT NULL,
  updated_at      TIMESTAMPTZ NOT NULL,
  CHECK (status IN ('draft', 'reviewed', 'supported', 'validated', 'active', 'rejected', 'retired'))
);
CREATE INDEX ix_mechanism_claims_project_status
  ON mechanism_claims(project_id, status, updated_at DESC);

CREATE TABLE mechanism_claim_versions (
  claim_id                 UUID NOT NULL REFERENCES mechanism_claims(claim_id) ON DELETE CASCADE,
  version                  INTEGER NOT NULL,
  name                     TEXT NOT NULL,
  mechanism_type           TEXT NOT NULL,
  statement                TEXT NOT NULL,
  expected_signature       TEXT,
  falsification_condition  TEXT NOT NULL,
  evidence_level           TEXT NOT NULL,
  created_by               TEXT NOT NULL,
  created_at               TIMESTAMPTZ NOT NULL,
  reviewed_by              TEXT,
  reviewed_at              TIMESTAMPTZ,
  content_hash             TEXT NOT NULL,
  PRIMARY KEY(claim_id, version),
  CHECK (mechanism_type IN ('qualitative', 'monotonic', 'threshold', 'interaction', 'temporal', 'constraint', 'failure-mode', 'executable-model'))
);

CREATE TABLE mechanism_claim_variables (
  claim_id       UUID NOT NULL,
  claim_version INTEGER NOT NULL,
  variable_code TEXT NOT NULL,
  variable_role TEXT NOT NULL,
  direction     TEXT,
  delay_ms      BIGINT,
  unit          TEXT NOT NULL,
  PRIMARY KEY(claim_id, claim_version, variable_code, variable_role),
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE
);

CREATE TABLE mechanism_claim_applicability (
  claim_id        UUID NOT NULL,
  claim_version  INTEGER NOT NULL,
  dimension_code TEXT NOT NULL,
  dimension_value TEXT NOT NULL,
  PRIMARY KEY(claim_id, claim_version, dimension_code, dimension_value),
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE
);

CREATE TABLE mechanism_claim_constraints (
  constraint_id  UUID PRIMARY KEY,
  claim_id       UUID NOT NULL,
  claim_version INTEGER NOT NULL,
  variable_code TEXT NOT NULL,
  constraint_kind TEXT NOT NULL,
  minimum        DOUBLE PRECISION,
  maximum        DOUBLE PRECISION,
  unit           TEXT NOT NULL,
  severity       TEXT NOT NULL,
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE,
  CHECK (minimum IS NOT NULL OR maximum IS NOT NULL),
  CHECK (minimum IS NULL OR maximum IS NULL OR minimum <= maximum),
  CHECK (severity IN ('hard', 'soft'))
);

CREATE TABLE mechanism_claim_evidence (
  evidence_link_id UUID PRIMARY KEY,
  claim_id         UUID NOT NULL,
  claim_version   INTEGER NOT NULL,
  evidence_kind   TEXT NOT NULL,
  reference_id    TEXT NOT NULL,
  polarity        TEXT NOT NULL,
  content_hash    TEXT NOT NULL,
  created_at      TIMESTAMPTZ NOT NULL,
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE,
  CHECK (polarity IN ('supporting', 'opposing'))
);

CREATE TABLE mechanism_claim_reviews (
  review_id      UUID PRIMARY KEY,
  claim_id       UUID NOT NULL,
  claim_version INTEGER NOT NULL,
  decision       TEXT NOT NULL,
  reviewer_id    TEXT NOT NULL,
  comment        TEXT,
  reviewed_at    TIMESTAMPTZ NOT NULL,
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE,
  CHECK (decision IN ('approve', 'reject'))
);

CREATE TABLE mechanism_claim_conflicts (
  conflict_id        UUID PRIMARY KEY,
  project_id         UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  left_claim_id      UUID NOT NULL,
  left_claim_version INTEGER NOT NULL,
  right_claim_id     UUID NOT NULL,
  right_claim_version INTEGER NOT NULL,
  conflict_kind      TEXT NOT NULL,
  rationale          TEXT NOT NULL,
  status             TEXT NOT NULL,
  created_by         TEXT NOT NULL,
  created_at         TIMESTAMPTZ NOT NULL,
  resolved_by        TEXT,
  resolved_at        TIMESTAMPTZ,
  resolution         TEXT,
  FOREIGN KEY(left_claim_id, left_claim_version) REFERENCES mechanism_claim_versions(claim_id, version),
  FOREIGN KEY(right_claim_id, right_claim_version) REFERENCES mechanism_claim_versions(claim_id, version),
  CHECK (left_claim_id <> right_claim_id),
  CHECK (status IN ('open', 'resolved'))
);
CREATE INDEX ix_mechanism_claim_conflicts_project
  ON mechanism_claim_conflicts(project_id, status, created_at DESC);

CREATE TABLE recommendation_knowledge_usage (
  recommendation_id UUID NOT NULL,
  claim_id          UUID NOT NULL,
  claim_version     INTEGER NOT NULL,
  usage_type        TEXT NOT NULL,
  content_hash      TEXT NOT NULL,
  PRIMARY KEY(recommendation_id, claim_id, claim_version, usage_type),
  FOREIGN KEY(claim_id, claim_version) REFERENCES mechanism_claim_versions(claim_id, version)
);
