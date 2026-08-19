-- 运行边界识别表
-- 存储事件驱动的运行边界识别结果。
-- 每个唯一的 (site_id, source_execution_id) 对应最多一个完整的运行记录。

CREATE TABLE IF NOT EXISTS process_execution_boundaries (
    -- 主键：平台生成的 UUIDv7，不同于源系统的 ExecutionId
    execution_id TEXT NOT NULL PRIMARY KEY,

    -- 分区键和隔离键
    site_id TEXT NOT NULL,
    edge_id TEXT NOT NULL,

    -- 来自生产事件的 ExecutionId（源标识，可能重复或缺失）
    source_execution_id TEXT NOT NULL,

    -- 时间戳
    started_at TIMESTAMP WITH TIME ZONE NOT NULL,
    ended_at TIMESTAMP WITH TIME ZONE,

    -- 运行状态：0=InProgress, 1=Completed, 2=Discarded
    status SMALLINT NOT NULL DEFAULT 0,

    -- 事件统计
    event_count INTEGER NOT NULL DEFAULT 0,

    -- 事件序列范围（按 Platform 的 IngestId）
    min_ingest_id BIGINT NOT NULL,
    max_ingest_id BIGINT NOT NULL,

    -- 边界置信度：0=Complete, 1=InferredEnd, 2=Fragmented
    confidence SMALLINT NOT NULL DEFAULT 0,

    -- 置信度说明（为什么是该等级）
    confidence_reason TEXT,

    -- 最后观察到该运行的事件时间
    last_observed_at TIMESTAMP WITH TIME ZONE NOT NULL,

    -- 审计时间戳
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- 查询索引
CREATE INDEX IF NOT EXISTS idx_execution_boundaries_site_source
    ON process_execution_boundaries(site_id, source_execution_id DESC);

CREATE INDEX IF NOT EXISTS idx_execution_boundaries_site_time
    ON process_execution_boundaries(site_id, started_at DESC);

-- 防止 (site_id, source_execution_id) 对的重复
CREATE UNIQUE INDEX IF NOT EXISTS idx_execution_boundaries_site_source_unique
    ON process_execution_boundaries(site_id, source_execution_id)
    WHERE status != 2; -- 不包括 Discarded 状态的记录

-- 自动更新 updated_at 触发器
CREATE OR REPLACE FUNCTION update_execution_boundaries_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_execution_boundaries_updated_at ON process_execution_boundaries;
CREATE TRIGGER trg_execution_boundaries_updated_at
    BEFORE UPDATE ON process_execution_boundaries
    FOR EACH ROW
    EXECUTE FUNCTION update_execution_boundaries_updated_at();
