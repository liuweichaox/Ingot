# Ingot 文档

Ingot 是开源工艺研发与优化系统。组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。

从本地启动到第一份建议，再到长期运维，按你的任务选择阅读路径。文档与仓库代码一同维护；已实现能力以[当前状态](status.md)为准，计划以[发展规划](project-plan.md)为准。

业务阅读顺序：工艺配置 → 现场接入 → 生产运行 → 质量管理 → 工艺追因 → 配方优化。下一份配方建议由工程师确认，再通过既有现场流程进入下一轮运行。

## 第一次使用

1. 阅读[当前状态](status.md)，确认系统能力、所需数据和使用边界。
2. 按[快速开始](getting-started.md)启动完整栈，检查服务健康并登录工作台。
3. 按[配方优化试点指南](pilot.md)核对已发布配方、真实运行与复核质量结果，完成一次建议、工程师决定、实际运行和结果冻结。

没有现场连接器也可以启动系统。当前推荐流程仍需要符合准入条件的真实运行证据；启动成功不表示已经具备生成建议的数据。

## 按任务阅读

- **工艺与研发工程师**：[试点指南](pilot.md)说明操作顺序；[分析与优化](optimization.md)解释为何生成或停止建议；[术语表](glossary.md)统一业务对象的含义。
- **接入工程师**：[数据接入](data-connection.md)说明身份、来源与映射；[系统设计](design.md)解释 Edge 与 Platform 的责任边界。
- **部署维护者**：先用[生产架构](production-architecture.md)确定可靠性范围，再按[部署运维](deployment.md)配置、备份和升级；发生故障时查阅[故障排查](troubleshooting.md)。
- **贡献者**：从[贡献指南](../CONTRIBUTING.md)准备开发环境；用[系统设计](design.md)和[数据模型](data-model.md)核对模块与记录归属；文档变更遵循[文档维护指南](documentation-guide.md)。

## 文档目录

### 入门

- [快速开始](getting-started.md)：环境准备、完整栈启动、健康检查和首次登录。
- [当前状态](status.md)：已实现能力、验证范围和部署者责任。
- [配方优化试点指南](pilot.md)：完成第一条可审计的建议与结果链。

### 操作指南

- [数据接入](data-connection.md)：接入身份、点位、映射及运行质量准入。
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
