-- A production context may omit tooling when a scenario does not require tooling traceability.
-- Existing non-null references and their foreign-key validation remain unchanged.
ALTER TABLE production_contexts
    ALTER COLUMN tooling_installation_id DROP NOT NULL;
