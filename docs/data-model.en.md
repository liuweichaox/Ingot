# Data Model Overview

This page lists Ingot's key PostgreSQL business relations and their responsibilities. Ordered SQL migrations maintain the database schema.

See [data integration](data-connection.en.md#site-mapping-and-local-storage) for site adapter mapping and local records. Platform relations are listed here.

In this document, “site” means a factory or independent production site, identified by `SiteId`; one factory may have multiple Edges.

## 1. Business Boundaries

The system's data trail must remain separate:

```text
Production event / actual execution / inspection facts
  -> optimization observation -> next-recipe recommendation -> engineer decision
      ├─ adoption or modification -> execution link -> frozen source outcome
      └─ rejection -> keep the decision and close the round
```

Real runs that meet the admission criteria can form later observations.

Next-recipe recommendations, mechanism claims, and conflicts belong to “factory/production site + recipe”; process-knowledge sources are authorized by factory/production site and may have an optional recipe-applicability selector in `knowledge_source_context`. Source reads apply site authorization first, then filter by recipe applicability. Analysis evidence comes from actual executions; recommendations, decisions, execution links, and outcomes are stored as separate append-only records. See “Recipe recommendation architecture constraints” in [System design](design.en.md).

## 2. Key Relations

| Group | Relations | Formal responsibility |
| --- | --- | --- |
| Identity and access | `users`, `user_sessions` | User identity, sessions, and site authorization. |
| Site and connectivity | `platform_edges`, `edge_runtime_status_history` | Edge identity, heartbeat, and runtime status. |
| Event idempotency and raw facts | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | Ingest deduplication ledger, immutable event envelopes, point catalogue, and high-frequency samples. |
| Execution and derived analysis | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | Execution boundaries plus recomputable phases, features, and materialization state. |
| Process configuration | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | Versioned process semantics, signals/features, specifications, and scenario policy. |
| Inspection facts | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | Inspection master data, measurements, attachments, reviews, and audit. |
| Tooling and production context | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | Tooling lineage, installation/counters, and execution context snapshots; production contexts link tooling only when the scenario needs it. |
| Model service configuration | `model_service_configurations` | External model-service configuration; user paths for training datasets, process models, evaluations, drift, and data-quality reports are retired. |
| Daily next recipe | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_execution_corrections`, `research_recipe_recommendation_decision_outcomes` | Factory/production-site + recipe scoped frozen recommendation (including the brief and `BriefHash`), knowledge use, engineer decision, later execution link and its correction history, and source outcome. Decisions, execution links, corrections, and outcomes are append-only. |
| Mechanism knowledge | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `knowledge_fragment_embeddings`, `knowledge_embedding_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions`, `research_asset_audit` | Factory/production-site scoped sources, extracted fragments, rebuildable retrieval indexes and jobs; factory/production-site + recipe scoped reviewable claims, constraints, and conflicts; models, fusion definitions, and knowledge-asset audit. Retrieval embeddings support discovery; formal records carry review state and business evidence. |
| Agent conversations and model traces | `agent_runs`, `agent_stream_events`, `chat_conversations`, `chat_messages` | Model traces and durable chat; user paths for problem cases and case evaluations are retired. |
| Operation-object cache | `data_object_operation_keys`, `data_object_summaries` | External-object operation keys and bounded summary cache. |

Upgrade note: the former `/api/v1/training-datasets` endpoints have been removed and return 404 after upgrade; there is no replacement export endpoint. Before upgrading, export required training dataset records through the previous API or preserve a database backup, and restrict access to exports and backups. Historical tables remain, but the current application does not read or write these records.

Check older databases that have yet to apply migration 0026. The migration stops if any of the legacy project container, recipe recommendation, mechanism claim, mechanism conflict, or knowledge source tables contain records, because their factory and recipe ownership cannot be established reliably. If the check finds records, back up the database and arrange a controlled export according to retention requirements. Verify migration and recovery on an isolated copy before planning the production upgrade.

Site Edge adapters manage device protocols, sampling schedules, and local point bindings. Platform uses `collection_points` to catalogue points and `process_sample_frames` with `process_sample_values` to store received production samples.

`process_sample_values.quality_code` stores point quality, and `quality_evidence_version` identifies the acquisition contract version. Platform reads missing or unrecognized contract versions as uncertain; valid versioned events provide point-quality evidence for analysis.

Model-service configuration is authorized for platform administrators. Recommendations and mechanism claims use `site_code` and `process_specification_id`; knowledge sources use `site_code`, and child records inherit their parent's site scope. Production facts use source-site authorization.

Within a site, `knowledge_sources` enforces one record per content SHA-256, with one immutable applicability selector. Omitting the `process-specification` selector defines a site-wide source; setting it defines a source for the matching recipe. Re-uploads retain the registered scope, and scope revisions use a new content revision. With explicit recipe context, the assistant searches site-wide and matching-recipe sources; without it, the assistant searches site-wide sources.

The recommendation's recipe version is retained in the frozen brief context; relational ownership remains factory/production site + recipe. Adopting a next-run correction records the engineer's decision against the current recipe version; a significant change is published through a separate revision draft.

## 3. Current Relationships

Daily recommendations, engineer decisions, actual execution links, and source outcomes use separate relations. A decision can precede an execution, and an outcome traces through its execution link. Sample values relate to frames and point catalogue entries through `frame_id`, `occurred_at`, and `point_key`.

## 4. Current Schema Considerations

1. `event_ingest_keys` provides ingest idempotency within its configured retention window. Deployments requiring long-term replay audits can add a durable tombstone or `(site_id, edge_id, seq)` watermark.
2. Index maintenance uses production `pg_stat_user_indexes` and `EXPLAIN` evidence and is delivered through migrations.

## 5. Write Rules

- Raw events, inspection records, and source evidence evolve through appended records or explicit supersede/review paths.
- Daily recommendations, decisions, execution links, and outcomes use stable business keys for retry idempotency.
- Decisions can precede an actual execution; outcomes freeze after execution, parameter readback, and inspection facts are complete.
- Cross-module reads pass through assemblers; optimizers receive data through formal analysis interfaces.
- Factory and recipe scope govern access and relationships for recommendations and mechanism evidence. The foreign keys from an engineer decision to its recommendation and from a mechanism conflict to its claims include `site_code`, `process_specification_id`, and the parent record ID. The foreign keys from an execution link to its decision and from a frozen outcome to its execution link each use `decision_id` alone and inherit scope through the parent records.

## 6. Using and changing the model

This page indexes relational responsibilities. The [migration directory](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql) defines database changes, and Migrator applies them in order during deployment.

When adding a model, define record ownership, the owning module's write path, append rules, and retry idempotency. Apply server authorization and database constraints together for cross-module relationships.

Validate successful writes, duplicate requests, site authorization, and existing-data migration. Back up the database, attachments, and keys before upgrades, then verify recovery and migration in isolation. See [Deployment](deployment.en.md) for recovery requirements.

Related entry points: [System design](design.en.md), [Mechanism knowledge and retrieval](optimization.en.md#mechanism-knowledge-and-retrieval), and [Contributing](../CONTRIBUTING.en.md).
