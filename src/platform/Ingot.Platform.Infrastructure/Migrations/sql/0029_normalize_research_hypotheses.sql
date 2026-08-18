-- Research hypotheses are first-class relational evidence, not JSON aggregates.

ALTER TABLE research_hypotheses
  ADD COLUMN statement TEXT,
  ADD COLUMN rationale TEXT,
  ADD COLUMN validation_outcome_code TEXT,
  ADD COLUMN expected_effect_direction TEXT,
  ADD COLUMN minimum_effect DOUBLE PRECISION,
  ADD COLUMN applicability TEXT,
  ADD COLUMN confidence DOUBLE PRECISION,
  ADD COLUMN created_by TEXT;

UPDATE research_hypotheses SET
  statement = payload->>'statement',
  rationale = payload->>'rationale',
  validation_outcome_code = payload->>'validationOutcomeCode',
  expected_effect_direction = payload->>'expectedEffectDirection',
  minimum_effect = NULLIF(payload->>'minimumEffect', '')::DOUBLE PRECISION,
  applicability = payload->>'applicability',
  confidence = (payload->>'confidence')::DOUBLE PRECISION,
  created_by = payload->>'createdBy';

CREATE TABLE research_hypothesis_variables (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  variable_code TEXT NOT NULL,
  PRIMARY KEY(hypothesis_id, sequence),
  UNIQUE(hypothesis_id, variable_code)
);
INSERT INTO research_hypothesis_variables(hypothesis_id, sequence, variable_code)
SELECT hypothesis.hypothesis_id, (item.ordinality - 1)::INTEGER, item.value
FROM research_hypotheses hypothesis
CROSS JOIN LATERAL jsonb_array_elements_text(
  COALESCE(hypothesis.payload->'variableCodes', '[]'::jsonb)) WITH ORDINALITY item(value, ordinality);

CREATE TABLE research_hypothesis_confounders (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  description TEXT NOT NULL,
  PRIMARY KEY(hypothesis_id, sequence)
);
INSERT INTO research_hypothesis_confounders(hypothesis_id, sequence, description)
SELECT hypothesis.hypothesis_id, (item.ordinality - 1)::INTEGER, item.value
FROM research_hypotheses hypothesis
CROSS JOIN LATERAL jsonb_array_elements_text(
  COALESCE(hypothesis.payload->'possibleConfounders', '[]'::jsonb)) WITH ORDINALITY item(value, ordinality);

CREATE TABLE research_hypothesis_causal_links (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  from_variable_code TEXT NOT NULL,
  to_variable_code TEXT NOT NULL,
  mechanism TEXT NOT NULL,
  direction TEXT,
  PRIMARY KEY(hypothesis_id, sequence),
  CHECK (from_variable_code <> to_variable_code)
);

CREATE TABLE research_hypothesis_temporal_features (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  variable_code TEXT NOT NULL,
  feature_code TEXT NOT NULL,
  phase_code TEXT,
  delay_ms BIGINT,
  window_ms BIGINT,
  PRIMARY KEY(hypothesis_id, sequence),
  CHECK (delay_ms IS NULL OR delay_ms >= 0),
  CHECK (window_ms IS NULL OR window_ms > 0)
);

CREATE TABLE research_hypothesis_interactions (
  interaction_id UUID PRIMARY KEY,
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  description TEXT NOT NULL,
  UNIQUE(hypothesis_id, sequence)
);
CREATE TABLE research_hypothesis_interaction_variables (
  interaction_id UUID NOT NULL REFERENCES research_hypothesis_interactions(interaction_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  variable_code TEXT NOT NULL,
  PRIMARY KEY(interaction_id, sequence),
  UNIQUE(interaction_id, variable_code)
);

CREATE TABLE research_hypothesis_failure_conditions (
  failure_condition_id UUID PRIMARY KEY,
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  condition TEXT NOT NULL,
  observable_signal TEXT NOT NULL,
  required_response TEXT NOT NULL,
  UNIQUE(hypothesis_id, sequence)
);

CREATE TABLE research_hypothesis_falsification_conditions (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  sequence INTEGER NOT NULL,
  condition TEXT NOT NULL,
  PRIMARY KEY(hypothesis_id, sequence)
);
INSERT INTO research_hypothesis_falsification_conditions(hypothesis_id, sequence, condition)
SELECT hypothesis_id, 0,
  '重复受控实验未观察到声明中的预期效应时，应推翻该历史假设：' || statement
FROM research_hypotheses;

CREATE TABLE research_hypothesis_evidence (
  hypothesis_id UUID NOT NULL REFERENCES research_hypotheses(hypothesis_id) ON DELETE CASCADE,
  evidence_id UUID NOT NULL,
  evidence_role TEXT NOT NULL,
  project_id UUID NOT NULL REFERENCES process_research_projects(project_id) ON DELETE CASCADE,
  kind TEXT NOT NULL,
  reference_id TEXT NOT NULL,
  summary TEXT NOT NULL,
  content_hash TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  PRIMARY KEY(hypothesis_id, evidence_id, evidence_role),
  CHECK (evidence_role IN ('supporting', 'opposing', 'validation'))
);

INSERT INTO research_hypothesis_evidence(
  hypothesis_id, evidence_id, evidence_role, project_id, kind,
  reference_id, summary, content_hash, created_at)
SELECT hypothesis.hypothesis_id, (item.value->>'evidenceId')::UUID, source.role,
  (item.value->>'projectId')::UUID, item.value->>'kind', item.value->>'referenceId',
  item.value->>'summary', item.value->>'contentHash', (item.value->>'createdAt')::TIMESTAMPTZ
FROM research_hypotheses hypothesis
CROSS JOIN LATERAL (VALUES
  ('supporting', hypothesis.payload->'supportingEvidence'),
  ('opposing', hypothesis.payload->'opposingEvidence'),
  ('validation', hypothesis.payload->'validationEvidence')) source(role, items)
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(source.items, '[]'::jsonb)) item(value);

DELETE FROM research_evidence WHERE resource_type = 'hypothesis';

ALTER TABLE research_hypotheses
  ALTER COLUMN statement SET NOT NULL,
  ALTER COLUMN rationale SET NOT NULL,
  ALTER COLUMN confidence SET NOT NULL,
  ALTER COLUMN created_by SET NOT NULL,
  DROP COLUMN payload,
  DROP CONSTRAINT research_hypotheses_status_check,
  ADD CONSTRAINT research_hypotheses_status_check
    CHECK (status IN ('proposed', 'selected', 'supported', 'validated', 'rejected', 'inconclusive')),
  ADD CONSTRAINT research_hypotheses_confidence_check CHECK (confidence BETWEEN 0 AND 1);
