# 模块说明

| 项目 | 职责 |
|---|---|
| `desktop` | Ingot Agent Desktop；Tauri 2、Rust 网络边界、React 19 界面和连接器代码生成工作台 |
| `Ingot.Agent` | 产品面隔离、模型中立运行状态机、类型化计划、预算、取消和验证 |
| `Ingot.Agent.Infrastructure` | 模型适配、SQLite 运行存储、工作区与制品工具 |
| `Ingot.Connector.Builder` | Actor 隔离源码、固定容器构建/测试、打包批准门和 SHA-256 ZIP |
| `Ingot.Connector.Host` | 标准 `ProductionEvent[]` 接入、SQLite 事件日志、上下文和 outbox |
| `Ingot.Central.Infrastructure` | 中心生产事实、检测、Webhook 与 Chat 只读工具 |
| `Ingot.Central.Api` | `/api/v1/chat/*`、桌面专用 `/api/v1/agent/*`、事实 HTTP、鉴权和 SSE |
| `Ingot.Central.Web` | Chat、事件、检测、日志和指标界面；不包含 Agent 代码生成入口 |
| `Ingot.Contracts` | Chat、Agent、连接器、事件和检测 HTTP 契约 |
| `Ingot.Domain` | 生产事件、对象引用和领域校验 |
| `Ingot.Application` | 事件日志、上报与上下文抽象 |
| `Ingot.Infrastructure` | SQLite 事件、outbox、中心上报、上下文、日志和指标 |
| `site` / `docs-site` | 官网与静态文档站 |

依赖关系由 `scripts/verify-architecture.sh` 校验。产品命名和公开范围由 `scripts/verify-product-scope.sh` 校验。
