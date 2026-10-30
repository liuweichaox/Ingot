-- 为机理资料管理和已审核来源查询提供站点内稳定排序索引。
CREATE INDEX IF NOT EXISTS ix_knowledge_sources_site_uploaded_source
    ON knowledge_sources(site_code, uploaded_at DESC, source_id DESC);

CREATE INDEX IF NOT EXISTS ix_knowledge_sources_site_reviewed_source
    ON knowledge_sources(site_code, (COALESCE(reviewed_at, updated_at)) DESC, source_id DESC)
    WHERE status = 'reviewed';
