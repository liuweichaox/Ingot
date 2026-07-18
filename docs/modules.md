# 模块说明

| 项目 | 职责 |
|---|---|
| `Ingot.Central.Api` | Central HTTP、事件接入、鉴权、查询、SSE 与 Chat API |
| `Ingot.Central.Infrastructure` | 中心生产事实、检测、Webhook 与 Chat 事实工具 |
| `Ingot.Contracts` | 事件、检测和 Chat 的公开 HTTP 契约 |
| `Ingot.Domain` | 生产事件、对象引用和领域校验 |
| `Ingot.Application` | 应用服务抽象 |
| `Ingot.Infrastructure` | 存储、日志、指标与运行时实现 |
| `Ingot.Central.Web` | 事实页面、检测、日志、指标和 Ingot Chat 界面 |
| `site` / `docs-site` | 官网与静态文档站 |

依赖关系由 `scripts/verify-architecture.sh` 校验。公开产品范围由 `scripts/verify-product-scope.sh` 校验。
