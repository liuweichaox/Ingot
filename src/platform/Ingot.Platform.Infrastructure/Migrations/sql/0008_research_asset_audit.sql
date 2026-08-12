DO $$
BEGIN
    IF to_regclass('public.process_improvement_audit') IS NOT NULL
       AND to_regclass('public.research_asset_audit') IS NULL THEN
        ALTER TABLE process_improvement_audit RENAME TO research_asset_audit;
    END IF;
END
$$;

DO $$
BEGIN
    IF to_regclass('public.idx_process_improvement_audit_resource') IS NOT NULL
       AND to_regclass('public.idx_research_asset_audit_resource') IS NULL THEN
        ALTER INDEX idx_process_improvement_audit_resource
            RENAME TO idx_research_asset_audit_resource;
    END IF;
END
$$;
