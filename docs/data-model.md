# 数据模型总览

本文是 Ingot PostgreSQL 正式关系表的职责清单。迁移按编号顺序执行，`0001_baseline.sql` 是历史起点，不应手工改写为“当前全量 schema”。

## 1. 业务边界

系统的数据主线必须保持分离：

```text
生产事件 / 实际运行 / 检验事实
  -> 优化观察 -> 下一配方建议 -> 工程师决定
  -> 实际运行关联 -> 源数据结果 -> 新一轮观察
```

下一配方建议、机理声明与冲突按“站点 + 配方”归属，工艺知识来源按站点归属；系统不设研发项目一类的容器，也不承载另一套运行计划或运行状态机。系统只对真实发生的运行取证；建议、决定、运行关联和结果都是独立的追加记录。约束全文见[系统设计](design.md)的“配方建议架构约束”。

## 2. 全表职责

| 表组 | 表 | 正式职责 |
| --- | --- | --- |
| 身份与访问 | `users`, `user_sessions` | 身份、会话与站点授权；不保存生产事实。 |
| 站点与接入 | `platform_edges`, `edge_runtime_status_history`, `data_source_instances`, `ingestion_task_templates`, `ingestion_task_bindings`, `ingestion_tasks`, `acquisition_probe_tasks` | Edge、数据源与采集任务的版本化配置和运行状态。 |
| 事件幂等与原始事实 | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | 事件接收去重账本、不可变事件信封、点位目录及高频样本事实。 |
| 执行与分析派生 | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | 从原始事件确定执行边界，并记录可重算的阶段、特征与物化状态。 |
| 工艺配置 | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | 稳定工艺语义、信号/特征、规范和场景策略的版本化定义。 |
| 检验事实 | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | 检验主数据、原始测量、附件、复核和审计；优化只经装配器读取有效记录。 |
| 工装与生产上下文 | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | 工装谱系、安装/计数与某次生产运行的上下文快照。 |
| 模型与数据集 | `training_dataset_versions`, `process_model_versions`, `model_evaluations`, `model_drift_readings`, `model_service_configurations`, `dataset_quality_validation_reports` | 训练数据、工艺模型、效果/漂移、外部模型服务配置和数据质量报告。 |
| 日常下一配方 | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_decision_outcomes` | 按站点 + 配方归属的冻结建议（含生成条件与 `BriefHash`）、采用知识、工程师决定、后续实际运行关联（实际运行键全局唯一）和源数据结果；后三者只追加，不能覆盖。 |
| 机理知识 | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `knowledge_fragment_embeddings`, `knowledge_embedding_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions`, `research_asset_audit` | 按站点归属的来源与抽取片段、可重建检索索引与任务，按站点 + 配方归属的可复核机理声明与约束/冲突，以及模型、融合定义和知识资产审计。嵌入不改变审核状态，也不是正式业务证据。 |
| Agent 对话与问题案例 | `agent_runs`, `agent_stream_events`, `problem_cases`, `case_level_evaluations`, `chat_conversations`, `chat_messages` | 模型调用轨迹、问题案例、案例评估和持久对话；不授予业务写权限。 |
| 操作对象缓存 | `data_object_operation_keys`, `data_object_summaries` | 外部对象的操作幂等键和受限摘要缓存。 |

训练数据集、工艺模型版本、模型评估和漂移记录目前是部署内共享的研发资产，读写按工艺工程师或平台管理员岗位授权；不能把它们视为已实施站点隔离的生产表。建议与机理声明通过 `site_code` 和 `process_specification_id` 归属；知识来源通过 `site_code` 归属，子记录从父记录继承范围。

建议所属配方版本保存在冻结生成条件的上下文中，关系表的归属键仍是站点 + 配方。工程师采用下一轮校正不会修改 `process_specification_versions`；显著变更通过独立修订草稿发布。

## 3. 关键迁移与约束

| 问题 | 风险 | 迁移处理 |
| --- | --- | --- |
| 日常决定、实际运行和质量结果曾放在同一 JSON 行中 | 不能表达“先决定、后运行”；结果写入会成为对决定行的更新 | 拆为 `decisions`、`decision_executions`、`decision_outcomes` 三张追加表；决定可无运行，结果必须已有运行关联。 |
| 研发项目是第二套归属容器 | 同一配方的建议与知识被项目切碎，项目删除还可能抹去证据 | 迁移 `0026` 删除项目、假设、操作域、项目知识声明与项目审计；建议、机理与知识改为站点 + 配方归属，存在旧项目数据时拒绝迁移。 |

## 4. 尚未自动改写的债务

1. `event_ingest_keys` 会按保留期清理，而 `production_events` 没有永久事件唯一约束。因此当前合同是“保留窗口内幂等”，不是无限期重放幂等。若现场需要长期审计，应增加永久轻量 tombstone 或 `(site_id, edge_id, seq)` 水位线。
2. 旧版数据库如仍有已退役表，应通过版本化迁移清理或归档；正式代码和产品接口不再读取它们。
3. 部分旧索引由新的分页索引前缀覆盖。生产库应基于 `pg_stat_user_indexes` 和 `EXPLAIN` 确认没有使用后再删除，不能在迁移中盲删。

## 5. 写入规则

- 原始事件、检验记录和来源证据只能新增或以显式 supersede/review 方式演进。
- 日常建议、工程师决定、实际运行关联和结果均用稳定业务唯一键实现重试幂等。
- 决定可以先于实际运行；结果只能在运行、参数回读和检验事实齐备后冻结。
- 所有跨模块读取必须通过装配器；优化器不能直连检验或设备表。
- 站点 + 配方是建议与机理证据的授权与关系边界；决定引用建议、冲突引用声明都用包含站点与配方的复合 FK，而不是仅靠应用检查。

## 6. 使用与变更检查

本页是关系表职责索引，不是可直接执行的建库脚本。实际 schema 由[迁移目录](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql)逐个演进；部署时由 Migrator 应用，不手工修改基线或跳过失败迁移。

新增数据模型前核对四件事：记录归属站点或部署、哪个模块拥有唯一写入路径、记录是否只能追加、重试如何保持幂等。为跨模块关系同时检查授权与数据库约束，不能把前端隐藏按钮当作权限边界。

变更验证应覆盖正常写入、重复请求、跨站点拒绝和迁移前旧数据处理。升级前备份数据库、附件与密钥，在隔离环境验证恢复和迁移；恢复方案见[部署运维](deployment.md)。

相关入口：[系统设计](design.md)、[机理知识设计](mechanism-knowledge.md)、[贡献指南](../CONTRIBUTING.md)。
