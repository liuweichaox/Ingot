# Ingot 文档

Ingot 是开源工艺研发与优化系统。组织配方版本、实验记录与运行证据，支持质量分析、工艺追因和配方优化。

每篇文档只负责一件事。产品措辞以[品牌规范](brand.md)为准，已实现能力以[当前状态](status.md)为准。

## 按任务阅读

| 目标 | 阅读 |
|---|---|
| 在本机运行 | [快速开始](getting-started.md) |
| 确认现在能做什么 | [当前状态](status.md) |
| 得到第一份下一份配方建议 | [配方优化试点指南](pilot.md) |
| 理解稳定边界 | [系统设计](design.md) |
| 接入现场数据 | [数据接入](data-connection.md) |
| 准备生产部署 | [生产架构](production-architecture.md)，然后[部署运维](deployment.md) |
| 参与开发 | [贡献指南](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.md) |

工作顺序是：工艺配置 → 现场接入 → 生产运行 → 质量管理 → 工艺追因 → 配方优化。下一份配方建议回到已发布配方版本，由工程师决定是否进入下一轮。

## 文档目录

### 开始

- [快速开始](getting-started.md)：启动本地完整栈
- [当前状态](status.md)：已实现能力与部署者责任
- [配方优化试点指南](pilot.md)：从真实运行到第一份建议

### 系统

- [系统设计](design.md)：业务模型与组件职责
- [分析与优化](optimization.md)：准入、覆盖范围和数值方法
- [机理知识设计](mechanism-knowledge.md)：知识如何约束建议

### 部署与接入

- [数据接入](data-connection.md)：身份、点位、映射和质量准入
- [部署运维](deployment.md)：配置、健康、备份和升级
- [生产架构](production-architecture.md)：故障模型与生产准入

### 参考

- [发展规划](project-plan.md)：建设优先级
- [场景评估边界](rollout.md)：部署者自行评估时的结论边界
- [常见问题](faq.md)
- [术语表](glossary.md)
- [品牌规范](brand.md)
- [开源依赖](open-source-dependencies.md)

English documentation starts at [index.en.md](index.en.md).
