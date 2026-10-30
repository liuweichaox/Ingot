# Ingot 文档

Ingot 是开源工艺研发与优化系统。组织配方版本、实验记录与运行证据，支持质量分析、工艺分析（包含工艺追因）和配方优化。

从本地启动到第一份建议，再到长期运维，按你的任务选择阅读路径。文档与仓库代码一同维护；已实现能力以[当前状态](status.md)为准，计划以[发展规划](project-plan.md)为准。

业务阅读顺序是：现场运行 → 质量检验 → 工艺分析 → 配方优化。工艺资料和机理知识属于工艺洞察，Ingot Chat 是跨模块的核对与调查入口，不是生产流程中的一步。

配置与接入为这条日常路径准备工艺语义和自动采集。用户界面使用“工厂/生产现场”作为数据与权限边界，系统内部用 `SiteId` 标识。下一份配方调整由工程师确认，再通过既有现场流程进入下一轮运行。

## 第一次使用

按目标选择入口；不需要先读完全部文档：

| 目标 | 路径 | 完成标志 |
|---|---|---|
| 本地启动并查看工作台 | [快速开始](getting-started.md) | 迁移成功、核心服务健康并能登录；不需要外部系统账号或设备连接 |
| 查看采集链路 | [快速开始](getting-started.md) → [半导体采集演示](semiconductor-demo.md) | 看到模拟运行数据进入工作台；数据均为合成演示数据，不代表真实设备或工艺结果 |
| 评估真实运行证据流程 | [当前状态](status.md) → [配方优化试点指南](pilot.md) | 用经授权的真实运行和复核质量结果检查准入，再完成建议与后续结果记录 |
| 参与代码或文档贡献 | [贡献指南](../CONTRIBUTING.md) | 按改动范围准备工具并完成相应检查；完整提交门禁见贡献指南 |

“当前状态”用于了解已实现能力和数据边界，可在启动前或之后阅读。没有现场连接器也可以启动系统；部署健康不表示已有符合准入条件的生产证据，也不表示系统已经能够生成建议。

## 按任务阅读

| 角色 | 阅读路径 |
|---|---|
| 工艺与研发工程师 | [试点指南](pilot.md) → [分析与优化](optimization.md) → [术语表](glossary.md) |
| 接入工程师 | [数据接入](data-connection.md) → [系统设计](design.md) |
| 部署与运维人员 | [生产架构](production-architecture.md) → [部署运维](deployment.md) → [故障排查](troubleshooting.md) |
| 贡献者 | [贡献指南](../CONTRIBUTING.md) → [系统设计](design.md)、[数据模型](data-model.md) → [文档维护指南](documentation-guide.md) |

## 文档目录

### 入门

- [快速开始](getting-started.md)：环境准备、完整栈启动、健康检查和首次登录。
- [当前状态](status.md)：已实现能力、验证范围和部署者责任。
- [配方优化试点指南](pilot.md)：完成第一条可审计的建议与结果链。

### 操作指南

- [数据接入](data-connection.md)：接入身份、点位、映射及运行质量准入。
- [半导体晶圆刻蚀采集演示](semiconductor-demo.md)：用独立模拟器展示过程数据、配方版本与 300 秒运行链路。
- [部署运维](deployment.md)：配置、模型服务、可观测性、备份和升级。
- [故障排查](troubleshooting.md)：按症状定位启动、授权、数据与建议问题。
- [场景评估边界](rollout.md)：用部署者自己的证据评估适用性与效果。

### 系统概念

- [系统设计](design.md)：组件职责、业务模型、依赖方向和架构约束。
- [分析与优化](optimization.md)：观察准入、安全边界、覆盖包络和方法选择。
- [机理知识设计](mechanism-knowledge.md)：来源复核、适用范围、约束和检索。
- [生产架构](production-architecture.md)：部署等级、故障模型与可靠性验收。

### 参考资料

- [数据模型总览](data-model.md)：正式表职责、追加记录、迁移与授权边界。
- [术语表](glossary.md)：运行、配方、证据和建议的统一定义。
- [常见问题](faq.md)：适用范围、数据条件和系统边界。
- [发展规划](project-plan.md)：建设优先级与完成条件。
- [品牌规范](brand.md)：产品定位、视觉资产和公开措辞。
- [开源依赖](open-source-dependencies.md)：主要组件、许可证与引入要求。
- [文档维护指南](documentation-guide.md)：文档分工、双语维护和验证方法。

## 支持与反馈

使用问题请附复现步骤、版本、服务状态和脱敏错误信息，在 [Issues](https://github.com/liuweichaox/Ingot/issues) 提交。方案讨论使用 [Discussions](https://github.com/liuweichaox/Ingot/discussions)。安全漏洞通过[安全策略](../SECURITY.md)中的私密渠道报告。

不要提交 `.env`、凭据、真实生产数据或可识别的现场材料。文档错误可直接提交 Pull Request，并同步对应英文页。

[English documentation](index.en.md)
