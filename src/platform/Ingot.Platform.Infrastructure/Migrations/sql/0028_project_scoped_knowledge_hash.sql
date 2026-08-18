-- The same immutable document may be cited independently by different research projects.
ALTER TABLE knowledge_sources
  DROP CONSTRAINT process_knowledge_sources_sha256_key,
  ADD CONSTRAINT ux_knowledge_sources_project_sha256 UNIQUE(project_id, sha256);
