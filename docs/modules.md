# 模块说明

## Domain

位置：`src/Ingot.Domain`

主要内容：

- `Models/DeviceConfig.cs`：v1/v2 数据源配置
- `Events/ProductionEvent.cs`：统一生产事件信封
- `Events/EventRule.cs`：事件派生规则
- `Events/EventQuery.cs`：本地事件查询条件
- `Events/ObjectRef.cs`：业务资产引用
- `Profiles/ProfileDefinition.cs`：行业 Profile 模型

Domain 不依赖 Hsl、InfluxDB、SQLite 或 ASP.NET。

## Application

位置：`src/Ingot.Application`

关键抽象：

- `IAcquisitionService`：数据源运行时编排与控制写入
- `IEventSink`：生产事件唯一写入口
- `IEventLog`：不可变事件日志
- `IEdgeContextStore`：资产上下文
- `IEdgeStateStore`：周期状态与上下文的组合边界
- `IEventRuleCollector`：事件规则采集
- `IEventShipper`：边缘事件批量上行
- `IProfileRegistry`：Profile 查询与配置验证
- PLC 客户端、驱动、队列、存储和指标接口

Application 只定义需要什么能力，不决定具体数据库或驱动库。

## Infrastructure

位置：`src/Ingot.Infrastructure`

### Acquisition

- `AcquisitionService`：按 `SourceCode` 管理运行时、热更新和成功写入后的 `parameter.applied`
- `ChannelCollector`：v1 遥测与 Conditional 周期事件
- `EventRuleCollector`：v2 `EventRules` 采集
- `EdgeStateStore`：active cycle 与 `context_state`
- `HeartbeatMonitor`：PLC 连接门控与状态

### Events

- `EventRuleEvaluator`：实现 `EdgePair`、`ValueChanged`、`BitFlag` 和 `Threshold`
- `EventSink`：先落盘，再执行可失败投影
- `SqliteEventLog`：事件表、索引、查询、游标和待上行状态
- `HttpEventShipper`：按序批量上行、确认后推进 outbox、指数退避

### Profiles

- `JsonProfileRegistry`：启动时加载 Profile 并校验 v2 配置引用

### Telemetry

- `QueueService`：内存批量聚合
- `InfluxDbDataStorageService`：默认遥测和事件投影存储

### DeviceConfigs / Clients / Logs / Metrics

- 配置加载、热更新和离线校验
- Hsl PLC 驱动选择与生命周期
- SQLite 日志
- Prometheus 与 `System.Diagnostics.Metrics`

## Edge Agent

位置：`src/Ingot.Edge.Agent`

职责：

- 组合所有默认实现
- 加载 `Configs` 与 `Profiles`
- 启动采集、中心心跳和事件上行后台服务
- 暴露事件、周期、上下文、采集控制、日志、指标和健康 API

`EventLogHealthCheck` 会实际访问事件库，并返回待上行事件数量。

## Contracts

位置：`src/Ingot.Contracts`

包括边缘注册/心跳、控制写入，以及 Edge/Central 事件批次和中心事件结果契约。控制写入以 `SourceCode` 为主字段，并兼容 v1 请求中的 `PlcCode`。

## Central API / Central Web

位置：

- `src/Ingot.Central.Api`
- `src/Ingot.Central.Web`

`Central.Api` 包含：

- per-edge token 校验与幂等批量摄入
- PostgreSQL 月度分区事件库
- JSONB 上下文过滤与 GIN 索引
- 跨 Edge 查询、SSE 和周期聚合
- PostgreSQL Webhook 订阅、CloudEvents 映射、过滤、游标和 HMAC 投递
- 节点注册、心跳、指标和日志代理

`Central.Web` 使用 Vue 3 + Vite，提供 Edges、Events、Metrics 和 Logs 页面。

## Tests

位置：`tests/Ingot.Core.Tests`

覆盖：

- 事件信封与 UUIDv7
- SQLite 事件持久化、过滤、序号和待上行状态
- EdgePair 语义
- 上下文与周期状态重启恢复
- Profile 约束
- v1/v2 配置校验
- 队列、驱动、日志等既有核心行为
- Edge token 校验与 EventShipper 确认语义
- CloudEvents 映射、订阅匹配与签名投递

## 推荐阅读顺序

1. `README.md`
2. `docs/design.md`
3. `src/Ingot.Edge.Agent/Program.cs`
4. `src/Ingot.Infrastructure/Events/EventSink.cs`
5. `src/Ingot.Infrastructure/Events/SqliteEventLog.cs`
6. `src/Ingot.Infrastructure/Acquisition/ChannelCollector.cs`
7. `src/Ingot.Infrastructure/Events/EventRuleCollector.cs`
8. `docs/rfc-production-events.md`
