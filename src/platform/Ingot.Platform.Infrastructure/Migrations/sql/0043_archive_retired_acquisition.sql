-- Move retired acquisition configuration out of the active schema without deleting historic evidence.
-- Moving the original relations preserves their rows, indexes, ownership, and foreign-key relationships.
CREATE SCHEMA IF NOT EXISTS ingot_legacy;

DO $$
DECLARE
    table_name text;
    active_table regclass;
    archived_table regclass;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'data_source_instances',
        'ingestion_task_templates',
        'ingestion_task_bindings',
        'acquisition_probe_tasks'
    ] LOOP
        active_table := to_regclass(format('%I.%I', 'public', table_name));
        archived_table := to_regclass(format('%I.%I', 'ingot_legacy', table_name));
        IF active_table IS NOT NULL AND archived_table IS NOT NULL THEN
            RAISE EXCEPTION 'Cannot archive %: both active and archived tables exist', table_name;
        ELSIF active_table IS NOT NULL THEN
            EXECUTE format('ALTER TABLE public.%I SET SCHEMA ingot_legacy', table_name);
        ELSIF archived_table IS NULL THEN
            RAISE EXCEPTION 'Cannot archive %: neither active nor archived table exists', table_name;
        END IF;
    END LOOP;
END $$;
