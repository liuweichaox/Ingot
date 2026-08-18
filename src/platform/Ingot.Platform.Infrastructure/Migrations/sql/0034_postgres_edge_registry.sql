CREATE TABLE platform_edges (
  edge_id TEXT PRIMARY KEY,
  host_base_url TEXT NULL,
  hostname TEXT NULL,
  version TEXT NULL,
  last_seen_at TIMESTAMPTZ NOT NULL,
  last_error TEXT NULL,
  acquisition_status JSONB NULL,
  delivery_status JSONB NULL
);

CREATE TABLE edge_runtime_status_history (
  edge_id TEXT NOT NULL REFERENCES platform_edges(edge_id) ON DELETE CASCADE,
  recorded_at TIMESTAMPTZ NOT NULL,
  acquisition_state TEXT NULL,
  last_valid_snapshot_at TIMESTAMPTZ NULL,
  valid_snapshot_count BIGINT NOT NULL,
  emitted_event_count BIGINT NOT NULL,
  acquisition_error TEXT NULL,
  delivery_state TEXT NULL,
  pending_event_count BIGINT NOT NULL,
  oldest_pending_event_at TIMESTAMPTZ NULL,
  backlog_capacity_used_percent DOUBLE PRECISION NULL,
  shipment_rate_per_second DOUBLE PRECISION NULL,
  delivery_error TEXT NULL,
  PRIMARY KEY(edge_id, recorded_at)
);

CREATE INDEX idx_edge_runtime_status_history_time
  ON edge_runtime_status_history(edge_id, recorded_at DESC);
