-- 为已复核工艺知识提供数据库侧关键词和向量混合检索；业务复核状态保持独立。
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS vector;

CREATE INDEX IF NOT EXISTS ix_knowledge_sources_project_reviewed
    ON knowledge_sources(project_id, updated_at DESC)
    WHERE status = 'reviewed';

CREATE INDEX IF NOT EXISTS ix_knowledge_fragments_reviewed_source
    ON knowledge_fragments(source_id, updated_at DESC)
    WHERE human_reviewed;

CREATE INDEX IF NOT EXISTS ix_knowledge_fragments_content_trgm
    ON knowledge_fragments USING gin (content gin_trgm_ops);

CREATE TABLE knowledge_fragment_embeddings (
    record_id uuid PRIMARY KEY,
    source_id uuid NOT NULL,
    content_hash text NOT NULL,
    embedding_model text NOT NULL,
    embedding_dimension integer NOT NULL,
    embedding vector(1536) NOT NULL,
    embedded_at timestamp with time zone NOT NULL,
    CONSTRAINT knowledge_fragment_embeddings_dimension_check CHECK (embedding_dimension = 1536),
    CONSTRAINT knowledge_fragment_embeddings_record_id_fkey
        FOREIGN KEY (record_id) REFERENCES knowledge_fragments(record_id) ON DELETE CASCADE,
    CONSTRAINT knowledge_fragment_embeddings_source_id_fkey
        FOREIGN KEY (source_id) REFERENCES knowledge_sources(source_id) ON DELETE CASCADE
);

CREATE INDEX ix_knowledge_fragment_embeddings_source_model
    ON knowledge_fragment_embeddings(source_id, embedding_model, embedded_at DESC);

CREATE INDEX ix_knowledge_fragment_embeddings_hnsw
    ON knowledge_fragment_embeddings USING hnsw (embedding vector_cosine_ops);

CREATE TABLE knowledge_embedding_jobs (
    source_id uuid PRIMARY KEY,
    requested_by text NOT NULL,
    embedding_model text NOT NULL,
    status text NOT NULL,
    available_at timestamp with time zone NOT NULL,
    lease_id uuid,
    lease_generation bigint NOT NULL DEFAULT 0,
    leased_at timestamp with time zone,
    attempt_count integer NOT NULL DEFAULT 0,
    last_error text,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT knowledge_embedding_jobs_status_check
        CHECK (status = ANY (ARRAY['queued', 'running', 'completed', 'dead-letter'])) ,
    CONSTRAINT knowledge_embedding_jobs_source_id_fkey
        FOREIGN KEY (source_id) REFERENCES knowledge_sources(source_id) ON DELETE CASCADE
);

CREATE INDEX ix_knowledge_embedding_jobs_claim
    ON knowledge_embedding_jobs(status, available_at, updated_at);
