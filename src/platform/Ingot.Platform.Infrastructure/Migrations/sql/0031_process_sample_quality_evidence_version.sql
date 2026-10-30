-- Existing values have no reliable evidence-version marker and are read as uncertain.
-- New projections declare the quality contract version that justified their quality code.
ALTER TABLE public.process_sample_values
    ADD COLUMN quality_evidence_version smallint DEFAULT 0 NOT NULL;

ALTER TABLE public.process_sample_values
    ADD CONSTRAINT ck_process_sample_values_quality_evidence_version
    CHECK (quality_evidence_version >= 0);
