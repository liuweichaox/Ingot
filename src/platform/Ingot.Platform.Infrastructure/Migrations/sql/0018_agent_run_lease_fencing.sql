-- 每次领取都递增 fencing generation。旧 Worker 即使恢复，也不能提交快照或事件。
ALTER TABLE public.agent_runs
  ADD COLUMN lease_generation BIGINT NOT NULL DEFAULT 0,
  ADD CONSTRAINT ck_agent_run_lease_generation CHECK (lease_generation >= 0);
