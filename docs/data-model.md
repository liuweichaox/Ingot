# 数据模型总览

本文是 Ingot PostgreSQL 正式关系表的职责清单。迁移按编号顺序执行，`0001_baseline.sql` 是历史起点，不应手工改写为“当前全量 schema”。

## 1. 业务边界

系统的数据主线必须保持分离：

```text
生产事件 / 实际运行 / 检验事实
  -> 优化观察 -> 下一配方建议 -> 工程师决定
  -> 实际运行关联 -> 源数据结果 -> 新一轮观察
```

研发项目保存问题、假设、证据和知识，但不承载另一套运行计划或运行状态机。系统只对真实发生的运行取证；建议、决定、运行关联和结果都是独立的追加记录。

## 2. 全表职责

| 表组 | 表 | 正式职责 |
| --- | --- | --- |
| 身份与访问 | `users`, `user_sessions`, `research_project_members` | 身份、会话及项目成员边界；不保存生产事实。 |
| 站点与接入 | `platform_edges`, `edge_runtime_status_history`, `data_source_instances`, `ingestion_task_templates`, `ingestion_task_bindings`, `ingestion_tasks`, `acquisition_probe_tasks` | Edge、数据源与采集任务的版本化配置和运行状态。 |
| 事件幂等与原始事实 | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | 事件接收去重账本、不可变事件信封、点位目录及高频样本事实。 |
| 执行与分析派生 | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | 从原始事件确定执行边界，并记录可重算的阶段、特征与物化状态。 |
| 工艺配置 | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | 稳定工艺语义、信号/特征、规范和场景策略的版本化定义。 |
| 检验事实 | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | 检验主数据、原始测量、附件、复核和审计；优化只经装配器读取有效记录。 |
| 工装与生产上下文 | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | 工装谱系、安装/计数与某次生产运行的上下文快照。 |
| 模型与数据集 | `training_dataset_versions`, `process_model_versions`, `model_evaluations`, `model_drift_readings`, `model_service_configurations`, `dataset_quality_validation_reports` | 训练数据、工艺模型、效果/漂移、外部模型服务配置和数据质量报告。 |
| 研发项目与审计 | `process_research_projects`, `process_research_audit`, `research_asset_audit`, `research_evidence` | 项目边界、业务审计和可验证证据索引。 |
| 日常下一配方 | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_decision_outcomes` | 冻结建议、采用知识、工程师决定、后续实际运行关联和源数据结果；后三者只追加，不能覆盖。 |
| 候选原因与研究资产 | `research_hypotheses`, `research_hypothesis_variables`, `research_hypothesis_causal_links`, `research_hypothesis_confounders`, `research_hypothesis_evidence`, `research_hypothesis_failure_conditions`, `research_hypothesis_falsification_conditions`, `research_hypothesis_interactions`, `research_hypothesis_interaction_variables`, `research_hypothesis_temporal_features`, `research_knowledge_claims` | 候选原因、证据限制及项目知识资产；不承载生产运行或日常建议。 |
| 机理知识 | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions` | 来源、抽取片段、可复核机理声明、约束/冲突、模型和融合定义。 |
| Agent 对话与问题案例 | `agent_runs`, `agent_stream_events`, `problem_cases`, `case_level_evaluations`, `chat_conversations`, `chat_messages` | 模型调用轨迹、问题案例、案例评估和持久对话；不授予业务写权限。 |
| 操作对象缓存 | `data_object_operation_keys`, `data_object_summaries` | 外部对象的操作幂等键和受限摘要缓存。 |

## 3. 本轮发现与已修复

| 问题 | 风险 | 本轮处理 |
| --- | --- | --- |
| 日常决定、实际运行和质量结果曾放在同一 JSON 行中 | 不能表达“先决定、后运行”；结果写入会成为对决定行的更新 | 拆为 `decisions`、`decision_executions`、`decision_outcomes` 三张追加表；决定可无运行，结果必须已有运行关联。 |
| 项目删除级联与“证据可回看”冲突 | 删除项目可能抹去正式证据 | 日常新增证据已在 DB 层禁止更新和删除；项目流程使用 archive。 |

## 4. 尚未自动改写的债务

1. `event_ingest_keys` 会按保留期清理，而 `production_events` 没有永久事件唯一约束。因此当前合同是“保留窗口内幂等”，不是无限期重放幂等。若现场需要长期审计，应增加永久轻量 tombstone 或 `(site_id, edge_id, seq)` 水位线。
2. 旧版数据库如仍有已退役表，应通过版本化迁移清理或归档；正式代码和产品接口不再读取它们。
3. 部分旧索引由新的分页索引前缀覆盖。生产库应基于 `pg_stat_user_indexes` 和 `EXPLAIN` 确认没有使用后再删除，不能在迁移中盲删。

## 5. 写入规则

- 原始事件、检验记录和来源证据只能新增或以显式 supersede/review 方式演进。
- 日常建议、工程师决定、实际运行关联和结果均用稳定业务唯一键实现重试幂等。
- 决定可以先于实际运行；结果只能在运行、参数回读和检验事实齐备后冻结。
- 所有跨模块读取必须通过装配器；优化器不能直连检验或设备表。
- 项目范围是每条研究证据的授权与关系边界，关系表必须用复合 FK 而不是仅靠应用检查。
