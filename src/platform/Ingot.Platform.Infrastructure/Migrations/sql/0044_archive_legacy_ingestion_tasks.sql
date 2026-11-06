-- Keep retired task payloads for historical provenance checks without exposing a writable active configuration table.
CREATE SCHEMA IF NOT EXISTS ingot_legacy;

DO $$
DECLARE
    active_table regclass := to_regclass('public.ingestion_tasks');
    archived_table regclass := to_regclass('ingot_legacy.ingestion_tasks');
BEGIN
    IF active_table IS NOT NULL AND archived_table IS NOT NULL THEN
        RAISE EXCEPTION 'Cannot archive ingestion_tasks: both active and archived tables exist';
    ELSIF active_table IS NOT NULL THEN
        ALTER TABLE public.ingestion_tasks SET SCHEMA ingot_legacy;
    ELSIF archived_table IS NULL THEN
        RAISE EXCEPTION 'Cannot archive ingestion_tasks: neither active nor archived table exists';
    END IF;
END $$;
