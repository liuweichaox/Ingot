SELECT create_hypertable(
  'production_events', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);

SELECT create_hypertable(
  'process_sample_frames', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);

SELECT create_hypertable(
  'process_sample_values', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);
