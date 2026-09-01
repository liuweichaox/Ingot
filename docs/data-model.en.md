# Data Model Overview

This is the responsibility inventory for Ingot's PostgreSQL relations. Migrations run in numeric order; `0001_baseline.sql` is a historical starting point, not a hand-maintained current-schema dump.

## 1. Business Boundaries

```text
Production event / actual execution / inspection facts
  -> optimization observation -> next-recipe recommendation -> engineer decision
  -> execution link -> source outcome -> next observation round
```

Research projects hold problems, hypotheses, evidence, and knowledge. They do not carry a second run-plan or run-state workflow. The system takes evidence only from actual executions; recommendations, decisions, execution links, and outcomes are separate append-only records.

## 2. Relation Inventory

| Group | Relations | Formal responsibility |
| --- | --- | --- |
| Identity and access | `users`, `user_sessions`, `research_project_members` | Identity, sessions, and project membership; never production facts. |
| Site and acquisition | `platform_edges`, `edge_runtime_status_history`, `data_source_instances`, `ingestion_task_templates`, `ingestion_task_bindings`, `ingestion_tasks`, `acquisition_probe_tasks` | Versioned Edge, source, and ingestion configuration and state. |
| Event idempotency and raw facts | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | Ingest deduplication ledger, immutable event envelopes, point catalogue, and high-frequency samples. |
| Execution and derived analysis | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | Execution boundaries plus recomputable phases, features, and materialization state. |
| Process configuration | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | Versioned process semantics, signals/features, specifications, and scenario policy. |
| Inspection facts | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | Inspection master data, measurements, attachments, reviews, and audit. |
| Tooling and production context | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | Tooling lineage, installation/counters, and execution context snapshots. |
| Models and datasets | `training_dataset_versions`, `process_model_versions`, `model_evaluations`, `model_drift_readings`, `model_service_configurations`, `dataset_quality_validation_reports` | Dataset/model versions, evaluation and drift, model-service configuration, and data-quality reports. |
| Research projects and audit | `process_research_projects`, `process_research_audit`, `research_asset_audit`, `research_evidence` | Project scope, business audit, and verifiable evidence index. |
| Daily next recipe | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_decision_outcomes` | Frozen recommendation, knowledge use, engineer decision, later execution link, and source outcome. The final three are append-only. |
| Candidate causes and research assets | `research_hypotheses`, `research_hypothesis_variables`, `research_hypothesis_causal_links`, `research_hypothesis_confounders`, `research_hypothesis_evidence`, `research_hypothesis_failure_conditions`, `research_hypothesis_falsification_conditions`, `research_hypothesis_interactions`, `research_hypothesis_interaction_variables`, `research_hypothesis_temporal_features`, `research_knowledge_claims` | Candidate causes, evidence limits, and project knowledge assets; never production runs or daily recommendations. |
| Mechanism knowledge | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions` | Sources, extracted fragments, reviewable claims, constraints/conflicts, models, and fusion definitions. |
| Agent conversations and problem cases | `agent_runs`, `agent_stream_events`, `problem_cases`, `case_level_evaluations`, `chat_conversations`, `chat_messages` | Model traces, problem cases, case evaluation, and durable chat; no business-write permission. |
| Operation-object cache | `data_object_operation_keys`, `data_object_summaries` | External-object operation keys and bounded summary cache. |

## 3. Findings and Fixes

| Finding | Risk | Fix in this change |
| --- | --- | --- |
| Daily decision, execution, and outcome shared one JSON row | Cannot represent decide-first/run-later; outcome mutates decision row | Split into `decisions`, `decision_executions`, and `decision_outcomes`; decision may have no run, outcome requires a link. |
| Cascade deletion conflicts with reviewable evidence | Deleting a project can erase formal evidence | New daily evidence rejects database update and delete; project workflow uses archive. |

## 4. Deferred Debt

1. `event_ingest_keys` is pruned while `production_events` lacks permanent event uniqueness. The current contract is retention-window idempotency, not indefinite replay idempotency. Long-term audit needs a durable tombstone or `(site_id, edge_id, seq)` watermark.
2. Older databases may still contain retired relations. Versioned migrations must clean up or archive them; formal code and product interfaces no longer read them.
3. Some older indexes are covered by newer pagination-index prefixes. Remove them only after production `pg_stat_user_indexes` and `EXPLAIN` evidence.

## 5. Write Rules

- Raw events, inspection records, and source evidence append or evolve through explicit supersede/review paths.
- Daily recommendations, decisions, execution links, and outcomes use stable business keys for retry idempotency.
- A decision may precede an actual execution; an outcome freezes only after execution, parameter readback, and inspection facts are complete.
- Cross-module reads pass through assemblers; optimizers do not directly read inspection or equipment tables.
- Project scope is both an authorization and relational boundary; relationship tables use composite FKs rather than application checks alone.
