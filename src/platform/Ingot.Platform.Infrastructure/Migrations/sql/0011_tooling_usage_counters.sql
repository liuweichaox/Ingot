CREATE TABLE IF NOT EXISTS tooling_usage_counters (
  tooling_installation_id UUID PRIMARY KEY,
  started_run_count BIGINT NOT NULL CHECK (started_run_count >= 0),
  updated_at TIMESTAMPTZ NOT NULL
);

INSERT INTO tooling_usage_counters(tooling_installation_id, started_run_count, updated_at)
SELECT (context->>'tooling_installation_id')::uuid, count(*), now()
FROM operation_context_snapshots
WHERE context->>'tooling_installation_id' ~*
      '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
GROUP BY (context->>'tooling_installation_id')::uuid
ON CONFLICT (tooling_installation_id) DO NOTHING;
