CREATE TABLE mechanism_claim_lifecycle_decisions (
  decision_id       UUID PRIMARY KEY,
  claim_id          UUID NOT NULL,
  claim_version     INTEGER NOT NULL,
  from_status       TEXT NOT NULL,
  to_status         TEXT NOT NULL,
  evidence_kind     TEXT,
  reference_id      TEXT,
  content_hash      TEXT,
  comment           TEXT,
  decided_by        TEXT NOT NULL,
  decided_at        TIMESTAMPTZ NOT NULL,
  FOREIGN KEY(claim_id, claim_version)
    REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE,
  CHECK (from_status IN ('reviewed', 'supported', 'validated', 'active')),
  CHECK (to_status IN ('supported', 'validated', 'active', 'retired')),
  CHECK ((evidence_kind IS NULL) = (reference_id IS NULL)),
  CHECK ((reference_id IS NULL) = (content_hash IS NULL)),
  CHECK (content_hash IS NULL OR content_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX ux_mechanism_claim_lifecycle_evidence
  ON mechanism_claim_lifecycle_decisions(claim_id, reference_id)
  WHERE reference_id IS NOT NULL;

CREATE INDEX ix_mechanism_claim_lifecycle_claim
  ON mechanism_claim_lifecycle_decisions(claim_id, decided_at DESC);
