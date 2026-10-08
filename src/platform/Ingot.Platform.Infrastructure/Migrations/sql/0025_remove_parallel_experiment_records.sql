-- Executions are the experiment records; retire the unused parallel storage.
-- Preserve unexpected data rather than silently discarding it during upgrade.
LOCK TABLE public.research_manual_experiment_records IN ACCESS EXCLUSIVE MODE;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM public.research_manual_experiment_records) THEN
        RAISE EXCEPTION 'Parallel record storage is not empty; retain and reconcile its data before retirement';
    END IF;
END;
$$;

DROP TABLE public.research_manual_experiment_records;
DROP FUNCTION public.reject_research_manual_experiment_record_mutation();
