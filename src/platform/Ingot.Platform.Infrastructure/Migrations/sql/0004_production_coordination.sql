-- 多站点读授权由认证声明和本地账户 site_ids 共同承载。
ALTER TABLE users
    ADD COLUMN site_ids text[] NOT NULL DEFAULT ARRAY[]::text[];

-- 设备探查由 Edge 主动领取。持久化任务允许创建、领取与完成落在不同 API 副本，
-- 同时保留短过期时间，避免重放已经失效的现场探查。
CREATE TABLE acquisition_probe_tasks (
    task_id text PRIMARY KEY,
    edge_id text NOT NULL,
    expected_protocol text NOT NULL,
    task_payload jsonb NOT NULL,
    result_payload jsonb,
    status text NOT NULL DEFAULT 'queued',
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    claimed_at timestamp with time zone,
    completed_at timestamp with time zone,
    CONSTRAINT acquisition_probe_tasks_status_check
        CHECK (status = ANY (ARRAY['queued'::text, 'claimed'::text, 'completed'::text])),
    CONSTRAINT acquisition_probe_tasks_claim_check
        CHECK ((status = 'queued') = (claimed_at IS NULL)),
    CONSTRAINT acquisition_probe_tasks_result_check
        CHECK ((status = 'completed') = (result_payload IS NOT NULL AND completed_at IS NOT NULL)),
    CONSTRAINT acquisition_probe_tasks_expiry_check CHECK (expires_at > created_at)
);

CREATE INDEX ix_acquisition_probe_tasks_claim
    ON acquisition_probe_tasks(edge_id, status, created_at);
CREATE INDEX ix_acquisition_probe_tasks_expiry
    ON acquisition_probe_tasks(expires_at);

-- 两类派生重算队列都必须有限重试。failed 是可审计终态；新的源事件到达时，
-- 摄入事务会重新排队并清除失败信息。
ALTER TABLE execution_boundary_recompute_jobs
    DROP CONSTRAINT execution_boundary_recompute_jobs_status_check,
    ADD COLUMN failed_at timestamp with time zone,
    ADD CONSTRAINT execution_boundary_recompute_jobs_status_check
        CHECK (status = ANY (ARRAY['queued'::text, 'running'::text, 'failed'::text])),
    ADD CONSTRAINT execution_boundary_recompute_jobs_failed_check
        CHECK ((status = 'failed') = (failed_at IS NOT NULL));

ALTER TABLE execution_analysis_recompute_jobs
    DROP CONSTRAINT execution_analysis_recompute_jobs_status_check,
    ADD COLUMN last_error text,
    ADD COLUMN failed_at timestamp with time zone,
    ADD CONSTRAINT execution_analysis_recompute_jobs_status_check
        CHECK (status = ANY (ARRAY['queued'::text, 'running'::text, 'failed'::text])),
    ADD CONSTRAINT execution_analysis_recompute_jobs_failed_check
        CHECK ((status = 'failed') = (failed_at IS NOT NULL));
