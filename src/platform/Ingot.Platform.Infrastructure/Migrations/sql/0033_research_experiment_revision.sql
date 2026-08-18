ALTER TABLE research_experiments
  ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

ALTER TABLE research_experiments
  ADD CONSTRAINT research_experiments_revision_positive CHECK (revision > 0);

UPDATE research_experiments
SET payload = jsonb_set(payload, '{revision}', to_jsonb(revision), true)
WHERE NOT (payload ? 'revision');
