-- PL/pgSQL variables named model_id and model_version collide with the
-- process_data_models columns inside the existence check, so every ingestion
-- template or task insert fails with "column reference model_id is ambiguous".
CREATE OR REPLACE FUNCTION guard_ingestion_data_model_reference()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    referenced_model_id TEXT;
    referenced_model_version TEXT;
BEGIN
    referenced_model_id := lower(trim(NEW.payload ->> 'dataModelId'));
    referenced_model_version := NEW.payload ->> 'dataModelVersion';
    IF referenced_model_id IS NULL OR referenced_model_id = '' THEN
        RETURN NEW;
    END IF;
    IF referenced_model_version !~ '^[1-9][0-9]*$' THEN
        RAISE EXCEPTION 'Invalid data model version in ingestion configuration payload'
            USING ERRCODE = '23514';
    END IF;

    PERFORM pg_advisory_xact_lock(
        hashtextextended('process-data-model:' || referenced_model_id || '@' || referenced_model_version, 0));
    IF NOT EXISTS (
        SELECT 1
        FROM process_data_models
        WHERE process_data_models.model_id = referenced_model_id
          AND process_data_models.version = referenced_model_version::integer) THEN
        RAISE EXCEPTION 'Referenced process data model does not exist'
            USING ERRCODE = '23503';
    END IF;
    RETURN NEW;
END;
$$;
