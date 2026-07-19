# 宏观架构

Ingot 由 Central API、Central Web、事实存储和使用方实现的数据源适配组成。Ingot 是可信生产事实与工艺调查平台；Central Web 中的 Ingot Chat 是工程师的主要入口，提供日常问答和可选的有界多 Agent 深入调查。数据源通过标准事件 API 接入。

```text
设备、仪器、业务系统或自定义数据源
  → 使用方适配与运行
  → POST /api/v1/events:batch
  → Central API
 → TimescaleDB（PostgreSQL + 时序扩展）生产事实
  → 查询、SSE 与 Central Web · Ingot Chat
```

如需现场本地持久化和 outbox，使用方可部署 `Ingot.Connector.Host` 作为可选入口：适配程序提交 `ProductionEvent[]` 到 Host，Host 再向 Central 批量上报。该运行方式由使用方拥有和运维。

## 产品边界

- Ingot Chat 运行在 Central Web，只查询事实、检查数据质量并返回证据；深入调查模式由工艺、质量和反证角色审查同一批已验证证据，不能访问设备或自行扩展数据范围。
- 使用方负责设备协议、字段映射、凭据、离线缓存、重试和本地进程监管。
- `Ingot.Connector.Host` 是可选的用户自管本地事件入口与 outbox；数据源适配的实现与运行由使用方负责。
- `POST /api/v1/events:batch` 接受经过令牌和契约校验的标准事件批次。
- `POST /api/v1/inspection-records` 接受独立检测事实。
- 事实查询、Chat 和 SSE 均通过 Central API 提供。
- 实时控制、安全联锁和设备写操作不属于 Ingot。

## 存储与网络

- TimescaleDB（PostgreSQL + 时序扩展）：中心生产事件、检测记录和查询事实。生产事件表为 hypertable，按 `occurred_at` 自动分块，并可按配置启用块级压缩与保留策略；幂等去重仍由独立的 `event_ingest_keys` 键表承担。本地自托管（Docker 镜像 `timescale/timescaledb`），无需外部托管服务。
- SQLite：部署可选的本地缓存、日志或边缘运行状态；亦作为离线/气隙单机的可选事实库形态。其运行方式由使用方选择。
- Chat 只经 Central 事实服务读取数据；模型配置不会改变数据权限或工具白名单。
- 生产环境应将 Central 与数据库置于受控网络边界，并使用 TLS、令牌轮换和最小化数据访问范围。

参见 [Ingot Chat](chat.md)、[生产事件规范](rfc-production-events.md)和[部署](tutorial-deployment.md)。
