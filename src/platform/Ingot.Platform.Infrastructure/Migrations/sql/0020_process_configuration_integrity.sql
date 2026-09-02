-- Make configuration references explicit where the registry has scalar columns.
-- NOT VALID preserves historical rows while enforcing every new or updated write.
ALTER TABLE process_specification_versions
    ADD CONSTRAINT fk_process_specification_versions_data_model
    FOREIGN KEY (data_model_id, data_model_version)
    REFERENCES process_data_models(model_id, version)
    ON DELETE RESTRICT
    NOT VALID;

ALTER TABLE process_analysis_plans
    ADD CONSTRAINT fk_process_analysis_plans_data_model
    FOREIGN KEY (data_model_id, data_model_version)
    REFERENCES process_data_models(model_id, version)
    ON DELETE RESTRICT
    NOT VALID;

ALTER TABLE scenario_packages
    ADD COLUMN analysis_plan_id TEXT NULL,
    ADD COLUMN analysis_plan_version INTEGER NULL;

UPDATE scenario_packages
SET analysis_plan_id = lower(trim(payload ->> 'analysisPlanId')),
    analysis_plan_version = CASE
        WHEN payload ->> 'analysisPlanVersion' ~ '^[1-9][0-9]*$'
            THEN (payload ->> 'analysisPlanVersion')::integer
        ELSE NULL
    END;

ALTER TABLE scenario_packages
    ADD CONSTRAINT ck_scenario_packages_analysis_plan_required
    CHECK (analysis_plan_id IS NOT NULL AND analysis_plan_version IS NOT NULL AND analysis_plan_version > 0)
    NOT VALID,
    ADD CONSTRAINT fk_scenario_packages_data_model
    FOREIGN KEY (data_model_id, data_model_version)
    REFERENCES process_data_models(model_id, version)
    ON DELETE RESTRICT
    NOT VALID,
    ADD CONSTRAINT fk_scenario_packages_analysis_plan
    FOREIGN KEY (analysis_plan_id, analysis_plan_version)
    REFERENCES process_analysis_plans(plan_id, version)
    ON DELETE RESTRICT
    NOT VALID;

CREATE INDEX idx_scenario_packages_analysis_plan
    ON scenario_packages(analysis_plan_id, analysis_plan_version);

-- Ingestion configurations keep their model reference inside JSON payloads. The
-- trigger shares the same advisory lock as model deletion, so a new reference
-- cannot be inserted between a reference check and the parent deletion.
CREATE OR REPLACE FUNCTION guard_ingestion_data_model_reference()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    model_id TEXT;
    model_version TEXT;
BEGIN
    model_id := lower(trim(NEW.payload ->> 'dataModelId'));
    model_version := NEW.payload ->> 'dataModelVersion';
    IF model_id IS NULL OR model_id = '' THEN
        RETURN NEW;
    END IF;
    IF model_version !~ '^[1-9][0-9]*$' THEN
        RAISE EXCEPTION 'Invalid data model version in ingestion configuration payload'
            USING ERRCODE = '23514';
    END IF;

    PERFORM pg_advisory_xact_lock(
        hashtextextended('process-data-model:' || model_id || '@' || model_version, 0));
    IF NOT EXISTS (
        SELECT 1
        FROM process_data_models
        WHERE process_data_models.model_id = model_id
          AND process_data_models.version = model_version::integer) THEN
        RAISE EXCEPTION 'Referenced process data model does not exist'
            USING ERRCODE = '23503';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_ingestion_tasks_data_model_reference
BEFORE INSERT OR UPDATE OF payload ON ingestion_tasks
FOR EACH ROW EXECUTE FUNCTION guard_ingestion_data_model_reference();

CREATE TRIGGER trg_ingestion_task_templates_data_model_reference
BEFORE INSERT OR UPDATE OF payload ON ingestion_task_templates
FOR EACH ROW EXECUTE FUNCTION guard_ingestion_data_model_reference();
