# Data Model Overview

This page lists Ingot's PostgreSQL relations and their responsibilities. Ordered SQL migrations maintain the database schema.

Site adapters can use the local tables in the [site acquisition data reference design](acquisition-data-design.en.md) as needed. Platform relations are listed here.

In this document, “site” means a factory or independent production site, identified by `SiteId`; one factory may have multiple Edges.

## 1. Business Boundaries

The system's data trail must remain separate:

```text
Production event / actual execution / inspection facts
  -> optimization observation -> next-recipe recommendation -> engineer decision
  -> execution link -> source outcome -> next observation round
```

Next-recipe recommendations, mechanism claims, and conflicts belong to “factory/production site + recipe”; process-knowledge sources are authorized by factory/production site and may have an optional recipe-applicability selector in `knowledge_source_context`. Source reads apply site authorization first, then filter by recipe applicability. Analysis evidence comes from actual executions; recommendations, decisions, execution links, and outcomes are stored as separate append-only records. See “Recipe recommendation architecture constraints” in [System design](design.en.md).

## 2. Relation Inventory

| Group | Relations | Formal responsibility |
| --- | --- | --- |
| Identity and access | `users`, `user_sessions` | User identity, sessions, and site authorization. |
| Site and connectivity | `platform_edges`, `edge_runtime_status_history` | Edge identity, heartbeat, and runtime status. |
| Event idempotency and raw facts | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | Ingest deduplication ledger, immutable event envelopes, point catalogue, and high-frequency samples. |
| Execution and derived analysis | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | Execution boundaries plus recomputable phases, features, and materialization state. |
| Process configuration | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | Versioned process semantics, signals/features, specifications, and scenario policy. |
| Inspection facts | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | Inspection master data, measurements, attachments, reviews, and audit. |
| Tooling and production context | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | Tooling lineage, installation/counters, and execution context snapshots; production contexts link tooling only when the scenario needs it. |
| Models and datasets | `training_dataset_versions`, `process_model_versions`, `model_evaluations`, `model_drift_readings`, `model_service_configurations`, `dataset_quality_validation_reports` | Dataset/model versions, evaluation and drift, model-service configuration, and data-quality reports. |
| Daily next recipe | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_decision_outcomes` | Factory/production-site + recipe scoped frozen recommendation (including the brief and `BriefHash`), knowledge use, engineer decision, later execution link (actual execution key is globally unique), and source outcome. The decision, execution-link, and outcome relations are append-only. |
| Mechanism knowledge | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `knowledge_fragment_embeddings`, `knowledge_embedding_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions`, `research_asset_audit` | Factory/production-site scoped sources, extracted fragments, rebuildable retrieval indexes and jobs; factory/production-site + recipe scoped reviewable claims, constraints, and conflicts; models, fusion definitions, and knowledge-asset audit. Retrieval embeddings support discovery; formal records carry review state and business evidence. |
| Agent conversations and problem cases | `agent_runs`, `agent_stream_events`, `problem_cases`, `case_level_evaluations`, `chat_conversations`, `chat_messages` | Model traces, problem cases, case evaluation, and durable chat; business writes are authorized through their owning modules. |
| Operation-object cache | `data_object_operation_keys`, `data_object_summaries` | External-object operation keys and bounded summary cache. |

Site Edge adapters manage device protocols, sampling schedules, and local point bindings. Platform uses `collection_points` to catalogue points and `process_sample_frames` with `process_sample_values` to store received production samples.

`process_sample_values.quality_code` stores point quality, and `quality_evidence_version` identifies the acquisition contract version. Platform reads missing or unrecognized contract versions as uncertain; valid versioned events provide point-quality evidence for analysis.

Training datasets, process-model versions, model evaluations, and drift readings are deployment-shared R&D assets with role-based access for process engineers and platform administrators. Recommendations and mechanism claims use `site_code` and `process_specification_id`; knowledge sources use `site_code`, and child records inherit their parent's site scope. Production facts use source-site authorization.

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
- Factory/production site + recipe defines authorization and relational scope for recommendations and mechanism evidence; composite foreign keys include `SiteId` and recipe identity.

## 6. Using and changing the model

This page indexes relational responsibilities. The [migration directory](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql) defines database changes, and Migrator applies them in order during deployment.

When adding a model, define record ownership, the owning module's write path, append rules, and retry idempotency. Apply server authorization and database constraints together for cross-module relationships.

Validate successful writes, duplicate requests, site authorization, and existing-data migration. Back up the database, attachments, and keys before upgrades, then verify recovery and migration in isolation. See [Deployment](deployment.en.md) for recovery requirements.

Related entry points: [System design](design.en.md), [Mechanism knowledge design](mechanism-knowledge.en.md), and [Contributing](../CONTRIBUTING.en.md).
