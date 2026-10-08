-- Standalone R&D projects can record measured experiments without manufacturing integrations.
CREATE TABLE public.research_manual_experiment_records (
    experiment_id uuid NOT NULL,
    project_id uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT research_manual_experiment_records_pkey PRIMARY KEY (experiment_id),
    CONSTRAINT research_manual_experiment_records_project_fkey
      FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id)
);

CREATE INDEX research_manual_experiment_records_project_recorded_idx
    ON public.research_manual_experiment_records(project_id, recorded_at DESC, experiment_id DESC);

-- Experiment facts are append-only, matching the audit and evidence contract.
CREATE FUNCTION public.reject_research_manual_experiment_record_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'research experiment records are append-only';
END;
$$;

CREATE TRIGGER research_manual_experiment_records_append_only
BEFORE UPDATE OR DELETE ON public.research_manual_experiment_records
FOR EACH ROW EXECUTE FUNCTION public.reject_research_manual_experiment_record_mutation();
