-- Fail-closed relational invariants for mechanism knowledge and recommendation usage.

ALTER TABLE mechanism_claim_variables
  ADD CONSTRAINT mechanism_claim_variables_role_check
    CHECK (variable_role IN ('cause', 'mediator', 'outcome', 'moderator')),
  ADD CONSTRAINT mechanism_claim_variables_direction_check
    CHECK (direction IS NULL OR direction IN ('increase', 'decrease', 'nonlinear')),
  ADD CONSTRAINT mechanism_claim_variables_delay_check
    CHECK (delay_ms IS NULL OR delay_ms >= 0);

ALTER TABLE mechanism_claim_constraints
  ADD CONSTRAINT mechanism_claim_constraints_kind_check
    CHECK (constraint_kind IN ('range', 'safe-range', 'preferred-range'));

ALTER TABLE mechanism_claim_versions
  ADD CONSTRAINT mechanism_claim_versions_hash_check
    CHECK (content_hash ~ '^[0-9a-f]{64}$');

ALTER TABLE mechanism_claim_evidence
  ADD CONSTRAINT mechanism_claim_evidence_hash_check
    CHECK (content_hash ~ '^[0-9a-f]{64}$');

ALTER TABLE mechanism_claims
  ADD CONSTRAINT ux_mechanism_claims_id_project UNIQUE(claim_id, project_id),
  DROP CONSTRAINT mechanism_claims_status_check,
  ADD CONSTRAINT mechanism_claims_status_check
    CHECK (status IN ('draft','reviewed','supported','validated','active','rejected','falsified','retired'));

ALTER TABLE mechanism_claim_conflicts
  ADD CONSTRAINT fk_mechanism_conflict_left_project
    FOREIGN KEY(left_claim_id, project_id) REFERENCES mechanism_claims(claim_id, project_id),
  ADD CONSTRAINT fk_mechanism_conflict_right_project
    FOREIGN KEY(right_claim_id, project_id) REFERENCES mechanism_claims(claim_id, project_id),
  ADD CONSTRAINT mechanism_conflict_resolution_check CHECK (
    (status = 'open' AND resolved_by IS NULL AND resolved_at IS NULL AND resolution IS NULL) OR
    (status = 'resolved' AND resolved_by IS NOT NULL AND resolved_at IS NOT NULL AND resolution IS NOT NULL));

CREATE UNIQUE INDEX ux_mechanism_conflict_pair
  ON mechanism_claim_conflicts(
    project_id,
    LEAST(left_claim_id, right_claim_id),
    GREATEST(left_claim_id, right_claim_id),
    (CASE WHEN left_claim_id < right_claim_id THEN left_claim_version ELSE right_claim_version END),
    (CASE WHEN left_claim_id < right_claim_id THEN right_claim_version ELSE left_claim_version END),
    conflict_kind)
  WHERE status = 'open';

ALTER TABLE recommendation_knowledge_usage
  ADD CONSTRAINT fk_recommendation_knowledge_usage_experiment
    FOREIGN KEY(recommendation_id) REFERENCES research_experiments(experiment_id) ON DELETE CASCADE,
  ADD CONSTRAINT recommendation_knowledge_usage_hash_check
    CHECK (content_hash ~ '^[0-9a-f]{64}$');

ALTER TABLE mechanism_claim_lifecycle_decisions
  ADD COLUMN validation_hypothesis_id UUID REFERENCES research_hypotheses(hypothesis_id),
  ADD COLUMN evaluation_outcome TEXT,
  ADD COLUMN evaluation_summary TEXT;

-- Decisions written before evidence binding cannot remain promoted. Preserve their audit rows,
-- but fail closed by returning affected claims to independent review.
UPDATE mechanism_claims claim
SET status = 'reviewed', updated_at = now()
WHERE claim.status IN ('supported', 'validated', 'active')
  AND EXISTS (
    SELECT 1 FROM mechanism_claim_lifecycle_decisions decision
    WHERE decision.claim_id = claim.claim_id
      AND decision.to_status IN ('supported', 'validated')
      AND decision.validation_hypothesis_id IS NULL);

ALTER TABLE mechanism_claim_lifecycle_decisions
  DROP CONSTRAINT mechanism_claim_lifecycle_decisions_from_status_check,
  DROP CONSTRAINT mechanism_claim_lifecycle_decisions_to_status_check,
  ADD CONSTRAINT mechanism_claim_lifecycle_decisions_from_status_check
    CHECK (from_status IN ('reviewed','supported','validated','active')),
  ADD CONSTRAINT mechanism_claim_lifecycle_decisions_to_status_check
    CHECK (to_status IN ('supported','validated','active','falsified','retired')),
  ADD CONSTRAINT mechanism_claim_lifecycle_transition_check CHECK (
    (from_status = 'reviewed' AND to_status = 'supported') OR
    (from_status = 'supported' AND to_status = 'validated') OR
    (from_status = 'validated' AND to_status = 'active') OR
    (from_status IN ('reviewed','supported','validated','active') AND to_status = 'falsified') OR
    (from_status = 'active' AND to_status = 'retired')),
  ADD CONSTRAINT mechanism_claim_lifecycle_evaluation_check CHECK (
    (to_status IN ('supported', 'validated') AND validation_hypothesis_id IS NOT NULL
      AND evaluation_outcome = 'supports' AND evaluation_summary IS NOT NULL) OR
    (to_status = 'falsified' AND validation_hypothesis_id IS NOT NULL
      AND evaluation_outcome = 'falsifies' AND evaluation_summary IS NOT NULL) OR
    (to_status NOT IN ('supported', 'validated', 'falsified') AND validation_hypothesis_id IS NULL
      AND evaluation_outcome IS NULL AND evaluation_summary IS NULL)) NOT VALID;

CREATE TABLE knowledge_extraction_jobs (
  source_id UUID PRIMARY KEY REFERENCES knowledge_sources(source_id) ON DELETE CASCADE,
  requested_by TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('queued', 'running', 'completed', 'failed')),
  attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
  available_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  lease_id UUID,
  leased_at TIMESTAMPTZ,
  last_error TEXT,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CHECK ((status = 'running') = (lease_id IS NOT NULL AND leased_at IS NOT NULL))
);
CREATE INDEX ix_knowledge_extraction_jobs_claim
  ON knowledge_extraction_jobs(status, available_at, updated_at);
