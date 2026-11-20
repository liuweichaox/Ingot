# 数据模型总览

本文列出 Ingot PostgreSQL 的主要业务关系表及其职责。数据库结构由按编号执行的 SQL 迁移维护。

现场适配器的映射和本地留存见[数据接入](data-connection.md#现场映射与本地存储)。本页说明 Platform 正式关系表。

本文中的“站点”指工厂或独立生产现场，由 `SiteId` 标识；一个工厂可以拥有多个 Edge。

## 1. 业务边界

系统的数据主线必须保持分离：

```text
生产事件 / 实际运行 / 检验事实
  -> 优化观察 -> 下一配方建议 -> 工程师决定
      ├─ 采用或修改 -> 实际运行关联 -> 冻结源数据结果
      └─ 拒绝 -> 保留拒绝决定，结束本轮
```

符合准入条件的真实运行可形成后续观察。

下一配方建议、机理声明与冲突按“工厂/生产现场 + 配方”归属，工艺知识来源按工厂/生产现场授权，并可在 `knowledge_source_context` 中带可选的配方适用范围。来源读取先应用站点授权，再按配方适用范围筛选。分析证据来自真实运行；建议、决定、运行关联和结果分别追加记录。约束全文见[系统设计](design.md)的“配方建议架构约束”。

## 2. 主要关系表职责

| 表组 | 表 | 正式职责 |
| --- | --- | --- |
| 身份与访问 | `users`, `user_sessions` | 用户身份、会话与站点授权。 |
| 站点与接入 | `platform_edges`, `edge_runtime_status_history` | Edge 身份、心跳和运行状态。 |
| 事件幂等与原始事实 | `event_ingest_keys`, `production_events`, `collection_points`, `process_sample_frames`, `process_sample_values` | 事件接收去重账本、不可变事件信封、点位目录及高频样本事实。 |
| 执行与分析派生 | `process_execution_boundaries`, `execution_boundary_recompute_jobs`, `execution_analysis_backfill_jobs`, `execution_analysis_recompute_jobs`, `execution_analysis_materializations`, `execution_features`, `execution_phases` | 从原始事件确定执行边界，并记录可重算的阶段、特征与物化状态。 |
| 工艺配置 | `process_data_models`, `signal_definitions`, `feature_definitions`, `phase_definitions`, `phase_mappings`, `process_specification_versions`, `scenario_packages`, `process_analysis_plans` | 稳定工艺语义、信号/特征、规范和场景策略的版本化定义。 |
| 检验事实 | `inspection_definitions`, `inspection_plans`, `inspection_scopes`, `inspection_records`, `inspection_attachments`, `inspection_reviews`, `inspection_audit_log` | 检验主数据、原始测量、附件、复核和审计；优化只经装配器读取有效记录。 |
| 工装与生产上下文 | `tooling_types`, `tooling_component_types`, `tooling_components`, `tooling_assemblies`, `tooling_assembly_revisions`, `tooling_installations`, `tooling_usage_counters`, `production_contexts`, `operation_context_snapshots` | 工装谱系、安装/计数与某次生产运行的上下文快照；生产上下文按场景需要关联工装。 |
| 模型服务配置 | `model_service_configurations` | 外部模型服务配置；训练数据集、工艺模型、评估、漂移和数据质量报告的用户路径已退役。 |
| 日常下一配方 | `research_recipe_recommendations`, `recipe_recommendation_knowledge_usage`, `research_recipe_recommendation_decisions`, `research_recipe_recommendation_decision_executions`, `research_recipe_recommendation_decision_outcomes` | 按工厂/生产现场 + 配方归属的冻结建议（含生成条件与 `BriefHash`）、采用知识、工程师决定、后续实际运行关联（实际运行键全局唯一）和源数据结果；决定、运行关联和结果均追加保存。 |
| 机理知识 | `knowledge_sources`, `knowledge_source_context`, `knowledge_fragments`, `knowledge_fragment_values`, `knowledge_extraction_jobs`, `knowledge_fragment_embeddings`, `knowledge_embedding_jobs`, `mechanism_claims`, `mechanism_claim_versions`, `mechanism_claim_applicability`, `mechanism_claim_constraints`, `mechanism_claim_evidence`, `mechanism_claim_reviews`, `mechanism_claim_lifecycle_decisions`, `mechanism_claim_variables`, `mechanism_claim_conflicts`, `mechanism_claim_forbidden_combinations`, `mechanism_claim_forbidden_combination_factors`, `mechanism_model_versions`, `mechanism_fusion_definitions`, `research_asset_audit` | 按工厂/生产现场归属的来源与抽取片段、可重建检索索引与任务，按工厂/生产现场 + 配方归属的可复核机理声明与约束/冲突，以及模型、融合定义和知识资产审计。检索嵌入服务于资料召回，审核状态和业务证据由正式记录维护。 |
| Agent 对话与模型调用 | `agent_runs`, `agent_stream_events`, `chat_conversations`, `chat_messages` | 模型调用轨迹和持久对话；问题案例与案例评估的用户路径已退役。 |
| 操作对象缓存 | `data_object_operation_keys`, `data_object_summaries` | 外部对象的操作幂等键和受限摘要缓存。 |

升级注意：原 `/api/v1/training-datasets` 接口已移除，升级后返回 404，且没有替代导出接口。需要保留训练数据集记录时，请在升级前通过旧版接口导出或保留数据库备份，并限制导出文件和备份的访问权限。历史表仍保留，但当前应用不读取或写入这些记录。

尚未执行 0026 号迁移的旧库需要预检。该迁移会在旧项目容器、配方建议、机理声明、机理冲突或知识来源任一表有记录时停止，避免将历史数据错误归到工厂和配方。若命中，先备份并按数据保留要求安排受控导出。在隔离副本验证迁移与恢复后，再确定生产升级方案。

现场 Edge 适配器负责设备协议、采样计划和本地点位绑定。Platform 使用 `collection_points` 目录化点位，并通过 `process_sample_frames` 与 `process_sample_values` 保存接收的生产采样事实。

`process_sample_values.quality_code` 保存点位质量，`quality_evidence_version` 标记使用的采集契约版本。Platform 将缺少或无法识别契约版本的质量按“不确定”处理；有效版本化事件可依据点位质量标记形成分析证据。

模型服务配置按平台管理员岗位授权。建议与机理声明通过 `site_code` 和 `process_specification_id` 归属；知识来源通过 `site_code` 归属，子记录从父记录继承范围。生产事实按来源站点授权读取。

`knowledge_sources` 的同站点内容 SHA-256 唯一；每个哈希对应一个不可变的适用范围选择器。省略 `process-specification` 选择器表示站点通用来源；设置该选择器表示匹配配方专用来源。重复上传沿用已登记范围，范围修订通过内容修订完成。配方明确时，助手检索站点通用来源和匹配配方来源；配方未明确时，检索站点通用来源。建议所属配方版本保存在冻结生成条件的上下文中，关系表的归属键仍是工厂/生产现场 + 配方。工程师采用下一轮校正后，决定记录关联当前配方版本；显著变更通过独立修订草稿发布。

## 3. 当前关系规则

日常建议、工程师决定、实际运行关联和来源结果分别保存。决定可先于实际运行，结果通过运行关联追溯到对应事实；采样值通过 `frame_id`、`occurred_at` 和 `point_key` 关联采样帧及点位目录。

## 4. 当前结构约束

1. `event_ingest_keys` 在配置的保留窗口内提供摄入幂等。需要长期重放审计的部署可增加永久轻量 tombstone 或 `(site_id, edge_id, seq)` 水位线。
2. 索引维护结合生产库 `pg_stat_user_indexes` 和 `EXPLAIN` 结果确定，并通过迁移执行。

## 5. 写入规则

- 原始事件、检验记录和来源证据通过新增记录或显式 supersede/review 方式演进。
- 日常建议、工程师决定、实际运行关联和结果使用稳定业务唯一键保持重试幂等。
- 决定可先于实际运行；运行、参数回读和检验事实齐备后冻结结果。
- 跨模块读取统一通过装配器；优化器的数据由正式分析接口提供。
- 建议与机理证据的访问和关联受工厂/生产现场及配方范围约束。工程师决定到建议、机理冲突到相关声明的复合外键包含 `site_code`、`process_specification_id` 和父记录标识。实际运行关联到决定、冻结结果到实际运行关联则使用单列 `decision_id` 外键，并通过父记录确定范围。

## 6. 使用与变更检查

本页是关系表职责索引；数据库结构由[迁移目录](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql)维护，并由 Migrator 按序应用。

新增数据模型时明确记录归属、唯一写入模块、追加规则和幂等键。跨模块关系同时落实服务端授权与数据库约束。

变更验证覆盖正常写入、重复请求、站点授权和现有数据迁移；升级前备份数据库、附件与密钥，并在隔离环境验证恢复和迁移。恢复方案见[部署运维](deployment.md)。

相关入口：[系统设计](design.md)、[机理知识与检索](optimization.md#机理知识与检索)、[贡献指南](../CONTRIBUTING.md)。
