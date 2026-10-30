-- Preserve the event's explicit applied-configuration reference on each typed sample frame.
ALTER TABLE public.process_sample_frames
    ADD COLUMN configuration_kind text,
    ADD COLUMN configuration_id text,
    ADD COLUMN configuration_version integer;

ALTER TABLE public.process_sample_frames
    ADD CONSTRAINT ck_process_sample_frames_configuration
    CHECK (
        (configuration_kind IS NULL AND configuration_id IS NULL AND configuration_version IS NULL)
        OR (
            configuration_kind IS NOT NULL
            AND configuration_kind ~ '^[a-z][a-z0-9_.-]{0,127}$'
            AND configuration_id IS NOT NULL
            AND configuration_id ~ '[^[:space:]]'
            AND char_length(configuration_id) <= 256
            AND configuration_version IS NOT NULL
            AND configuration_version > 0
        )
    );

CREATE INDEX ix_process_sample_frames_configuration
    ON public.process_sample_frames (
        configuration_kind,
        configuration_id,
        configuration_version,
        occurred_at DESC
    )
    WHERE configuration_kind IS NOT NULL;
