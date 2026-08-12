-- Keep the database invariant aligned with the evidence kinds accepted by the
-- research workflow. The original constraint predates execution comparisons
-- and transfer assessments.

ALTER TABLE research_evidence
  DROP CONSTRAINT research_evidence_kind_check;

ALTER TABLE research_evidence
  ADD CONSTRAINT research_evidence_kind_check
  CHECK (kind IN (
    'dataset-snapshot',
    'experiment-result',
    'analysis-run',
    'execution-comparison',
    'mechanism-model',
    'knowledge-source',
    'operating-region',
    'transfer-assessment'));
