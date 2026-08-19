-- 运行边界是生产事件的派生投影。摄入事务只登记重算请求，独立 Worker 通过租约领取，
-- 避免 HTTP 请求结束、进程退出或多副本并发造成投影静默丢失。

ALTER TABLE process_execution_boundaries
    ADD COLUMN IF NOT EXISTS gap_detected boolean NOT NULL DEFAULT false;

CREATE TABLE execution_boundary_recompute_jobs (
    site_id text NOT NULL,
    source_execution_id text NOT NULL,
    edge_id text NOT NULL,
    requested_max_ingest_id bigint NOT NULL,
    gap_detected boolean NOT NULL DEFAULT false,
    status text NOT NULL DEFAULT 'queued',
    attempt_count integer NOT NULL DEFAULT 0,
    available_at timestamp with time zone NOT NULL DEFAULT now(),
    lease_id uuid,
    leased_at timestamp with time zone,
    last_error text,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, source_execution_id),
    CONSTRAINT execution_boundary_recompute_jobs_status_check
        CHECK (status = ANY (ARRAY['queued'::text, 'running'::text])),
    CONSTRAINT execution_boundary_recompute_jobs_attempt_count_check
        CHECK (attempt_count >= 0),
    CONSTRAINT execution_boundary_recompute_jobs_lease_check
        CHECK ((status = 'running') = (lease_id IS NOT NULL AND leased_at IS NOT NULL))
);

CREATE INDEX ix_execution_boundary_recompute_jobs_claim
    ON execution_boundary_recompute_jobs(status, available_at, updated_at);
