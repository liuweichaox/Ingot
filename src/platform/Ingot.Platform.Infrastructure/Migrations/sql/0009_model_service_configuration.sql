CREATE TABLE public.model_service_configurations (
  entry_point       TEXT PRIMARY KEY,
  enabled           BOOLEAN NOT NULL,
  provider          TEXT NOT NULL,
  protocol          TEXT NOT NULL,
  base_url          TEXT NULL,
  fast_model        TEXT NOT NULL,
  reasoning_model   TEXT NOT NULL,
  protected_api_key TEXT NULL,
  api_key_hint      TEXT NULL,
  updated_at        TIMESTAMPTZ NOT NULL,
  updated_by        TEXT NOT NULL,
  CONSTRAINT ck_model_service_entry_point CHECK (entry_point = 'chat'),
  CONSTRAINT ck_model_service_protocol CHECK (protocol IN ('Responses', 'ChatCompletions')),
  CONSTRAINT ck_model_service_provider CHECK (length(provider) BETWEEN 1 AND 100),
  CONSTRAINT ck_model_service_models CHECK (
    length(fast_model) BETWEEN 1 AND 200 AND
    length(reasoning_model) BETWEEN 1 AND 200)
);
