CREATE TABLE public.chat_conversations (
  conversation_id UUID PRIMARY KEY,
  user_id          TEXT NOT NULL,
  title            TEXT NOT NULL,
  page_context     JSONB NULL,
  status           TEXT NOT NULL DEFAULT 'active',
  created_at       TIMESTAMPTZ NOT NULL,
  updated_at       TIMESTAMPTZ NOT NULL,
  last_message_at  TIMESTAMPTZ NOT NULL,
  archived_at      TIMESTAMPTZ NULL,
  version          BIGINT NOT NULL DEFAULT 1,
  CONSTRAINT ck_chat_conversation_status CHECK (status IN ('active', 'archived')),
  CONSTRAINT ck_chat_conversation_title CHECK (length(title) BETWEEN 1 AND 200),
  CONSTRAINT ck_chat_conversation_version CHECK (version > 0)
);

CREATE INDEX ix_chat_conversations_user_recent
  ON public.chat_conversations(user_id, last_message_at DESC, conversation_id DESC);

ALTER TABLE public.agent_runs
  ADD COLUMN conversation_id UUID NULL,
  ADD COLUMN trigger_message_id UUID NULL,
  ADD COLUMN response_message_id UUID NULL;

UPDATE public.agent_runs
SET conversation_id = CASE
  WHEN COALESCE(snapshot->>'conversationId', run_id) ~
       '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
    THEN COALESCE(snapshot->>'conversationId', run_id)::UUID
  ELSE gen_random_uuid()
END;

ALTER TABLE public.agent_runs ALTER COLUMN conversation_id SET NOT NULL;

CREATE INDEX ix_agent_runs_conversation_created
  ON public.agent_runs(conversation_id, created_at, run_id);

INSERT INTO public.chat_conversations(
  conversation_id, user_id, title, page_context, status,
  created_at, updated_at, last_message_at, version)
SELECT
  conversation_id,
  (array_agg(user_id ORDER BY created_at, run_id))[1],
  left((array_agg(COALESCE(snapshot->>'question', '对话') ORDER BY created_at, run_id))[1], 200),
  (jsonb_agg(snapshot->'pageContext' ORDER BY created_at, run_id))->0,
  'active',
  min(created_at),
  max(COALESCE(completed_at, created_at)),
  max(COALESCE(completed_at, created_at)),
  1
FROM public.agent_runs
GROUP BY conversation_id;

CREATE TABLE public.chat_messages (
  message_id         UUID PRIMARY KEY,
  conversation_id    UUID NOT NULL REFERENCES public.chat_conversations(conversation_id) ON DELETE CASCADE,
  sequence            BIGINT NOT NULL,
  role                TEXT NOT NULL,
  status              TEXT NOT NULL,
  text_content        TEXT NULL,
  answer              JSONB NULL,
  run_id              TEXT NULL REFERENCES public.agent_runs(run_id) ON DELETE SET NULL,
  client_message_id   UUID NULL,
  error               TEXT NULL,
  created_at          TIMESTAMPTZ NOT NULL,
  completed_at        TIMESTAMPTZ NULL,
  CONSTRAINT uq_chat_message_sequence UNIQUE (conversation_id, sequence),
  CONSTRAINT uq_chat_message_client_id UNIQUE (conversation_id, client_message_id),
  CONSTRAINT ck_chat_message_role CHECK (role IN ('user', 'assistant')),
  CONSTRAINT ck_chat_message_status CHECK (status IN ('pending', 'generating', 'completed', 'failed', 'cancelled')),
  CONSTRAINT ck_chat_message_sequence CHECK (sequence > 0),
  CONSTRAINT ck_chat_message_content CHECK (
    (role = 'user' AND text_content IS NOT NULL AND answer IS NULL) OR
    (role = 'assistant'))
);

CREATE INDEX ix_chat_messages_conversation_sequence
  ON public.chat_messages(conversation_id, sequence DESC);

CREATE INDEX ix_chat_messages_run
  ON public.chat_messages(run_id)
  WHERE run_id IS NOT NULL;

WITH ordered AS (
  SELECT
    run_id,
    conversation_id,
    snapshot,
    created_at,
    completed_at,
    row_number() OVER (PARTITION BY conversation_id ORDER BY created_at, run_id) AS turn_number
  FROM public.agent_runs
)
INSERT INTO public.chat_messages(
  message_id, conversation_id, sequence, role, status, text_content,
  run_id, created_at, completed_at)
SELECT
  gen_random_uuid(), conversation_id, turn_number * 2 - 1, 'user', 'completed',
  COALESCE(snapshot->>'question', '历史消息'), run_id, created_at, created_at
FROM ordered;

WITH ordered AS (
  SELECT
    run_id,
    conversation_id,
    snapshot,
    created_at,
    completed_at,
    row_number() OVER (PARTITION BY conversation_id ORDER BY created_at, run_id) AS turn_number
  FROM public.agent_runs
)
INSERT INTO public.chat_messages(
  message_id, conversation_id, sequence, role, status, text_content,
  answer, run_id, error, created_at, completed_at)
SELECT
  gen_random_uuid(),
  conversation_id,
  turn_number * 2,
  'assistant',
  CASE snapshot->>'status'
    WHEN 'completed' THEN 'completed'
    WHEN 'failed' THEN 'failed'
    WHEN 'cancelled' THEN 'cancelled'
    WHEN 'queued' THEN 'pending'
    ELSE 'generating'
  END,
  snapshot->'answer'->>'summary',
  snapshot->'answer',
  run_id,
  COALESCE(snapshot->>'error', snapshot->>'cancellationReason'),
  COALESCE((snapshot->>'startedAt')::TIMESTAMPTZ, created_at),
  completed_at
FROM ordered;

UPDATE public.agent_runs run
SET trigger_message_id = message.message_id
FROM public.chat_messages message
WHERE message.run_id = run.run_id AND message.role = 'user';

UPDATE public.agent_runs run
SET response_message_id = message.message_id
FROM public.chat_messages message
WHERE message.run_id = run.run_id AND message.role = 'assistant';

ALTER TABLE public.agent_runs
  ADD CONSTRAINT fk_agent_runs_conversation
    FOREIGN KEY (conversation_id) REFERENCES public.chat_conversations(conversation_id) ON DELETE RESTRICT,
  ADD CONSTRAINT fk_agent_runs_trigger_message
    FOREIGN KEY (trigger_message_id) REFERENCES public.chat_messages(message_id) ON DELETE RESTRICT,
  ADD CONSTRAINT fk_agent_runs_response_message
    FOREIGN KEY (response_message_id) REFERENCES public.chat_messages(message_id) ON DELETE RESTRICT;
