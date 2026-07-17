# 部署

本文说明如何将 Ingot 部署为一个长期运行、可观测、边缘优先的生产事件系统。

推荐部署原则如下：

- `Edge Agent` 是采集主程序，必须部署在靠近 PLC 的节点
- `InfluxDB` 是默认 TSDB 实现
- 遥测直接写 TSDB；生产事件先写本地 `events.db`
- `Central API / Central Web` 与 PostgreSQL 组成可选中心事件枢纽，不是采集前提

## 推荐部署拓扑

### 单节点

适合本地验证、实验室、单条产线：

- 1 个 `Edge Agent`
- 1 个 `InfluxDB`
- 可选 1 套 `PostgreSQL + Central API + Central Web`

### 多节点

适合多车间、多产线或多工厂：

- 每个采集节点部署自己的 `Edge Agent`
- 每个采集节点保留自己的事件事实库、业务上下文和日志
- 中心侧部署统一的 `PostgreSQL + Central API + Central Web`
- `InfluxDB` 可以集中部署，也可以按站点拆分

核心约束是：

- `Edge Agent` 必须在 PLC 可达的网络里
- 中心侧不可达不影响本地事件产生与查询

## 运行时组件

### Edge Agent

职责：

- 加载设备配置
- 建立 PLC 连接
- 执行 Always / Conditional 采集
- 按批次直接写 InfluxDB
- 派生并持久化生产事件
- 暴露本地事件、上下文、健康、日志和指标接口

### InfluxDB

职责：

- 作为默认时序存储实现

### Central API / Central Web

职责：

- 展示节点状态
- 查看心跳
- 聚合指标
- 代理边缘诊断接口
- 通过 per-edge token 摄入生产事件
- 在 PostgreSQL 中幂等汇聚、分区存储并按上下文查询
- 提供跨 Edge 事件流、SSE 和周期聚合

注意：

- 中心侧不可用时，采集主链路仍应继续运行
- 中心侧不是存储成功语义的一部分

## 推荐发布方式

生产环境推荐使用 `dotnet publish` 后的二进制部署，而不是直接在生产环境执行 `dotnet run`。

### 发布 Edge Agent

```bash
dotnet publish src/Ingot.Edge.Agent -c Release -o ./publish/edge
```

启动：

```bash
./publish/edge/Ingot.Edge.Agent
```

### 发布 Central API

```bash
dotnet publish src/Ingot.Central.Api -c Release -o ./publish/central-api
```

启动：

```bash
./publish/central-api/Ingot.Central.Api
```

### 构建 Central Web

```bash
cd src/Ingot.Central.Web
npm ci
npm run build
```

构建输出在 `dist/`，应由 nginx 或其他静态文件服务托管。

## 容器化边界

仓库里的 Compose 文件主要用于：

- `InfluxDB`
- `PostgreSQL`
- `Central API`
- `Central Web`

启动中心事件枢纽：

```bash
docker compose -f docker-compose.events.yml up -d --build
```

不建议把 `Edge Agent` 作为默认容器化部署模型写进主路径。

原因不是“不能容器化”，而是：

- Edge 需要稳定访问 PLC 网络
- 现场通常涉及真实网卡、VLAN、路由和防火墙
- 宿主机进程部署更容易排查网络问题

因此推荐策略是：

- 中心组件可以容器化
- `InfluxDB` 可以容器化
- `Edge Agent` 优先作为宿主机进程部署

## 运行数据目录

生产环境需要重点关注：

- `Data/logs.db`
- `Data/acquisition-state.db`
- `Data/events.db`

含义：

- `logs.db`：本地日志存储，默认保留 30 天
- `Logging:RetentionDays`：用于调整本地日志保留天数；设置为 `<= 0` 时关闭清理
- `acquisition-state.db`：条件采集的 active cycle 状态库
- `events.db`：不可变生产事件与待上行状态

这里不保存原始遥测的本地补偿副本；事件事实独立持久化。

`Events:MaxBacklogRows` 是断网积压的硬上限。触顶时系统会为审计事件预留空间、删除最旧待传事实并写入 `diagnostic.backlog_dropped`；`event_backlog_dropped_total` 记录累计丢弃数，`event_outbox_backlog` 以 gauge 暴露当前积压。每次上行失败还会递增对应行的 `ship_attempts`。

## 上线前配置检查

至少确认这些配置：

### 应用级

- `Urls`
- `Logging:DatabasePath`
- `Logging:RetentionDays`
- `InfluxDB:*`
- `Acquisition:DeviceConfigService:ConfigDirectory`
- `Acquisition:StateStore:DatabasePath`
- `Events:DatabasePath`
- `Events:RetentionDays`
- `Events:CleanupIntervalSeconds`
- `Events:MaxBacklogRows`
- `Profiles:Directory`
- `Edge:EnableCentralReporting`
- `Edge:CentralApiBaseUrl`
- `Edge:EnableEventShipping`
- `Edge:EdgeId`
- `Edge:EventIngestToken`
- `Edge:EventBatchSize`

中心侧还必须确认：

- `ConnectionStrings:Events`
- `EventIngest:RequireToken`
- `EventIngest:EdgeTokens:{EdgeId}`
- `Webhook:Enabled`
- `Webhook:PollIntervalMs`
- `Webhook:RequestTimeoutSeconds`
- `Webhook:EventTypePrefix`

不要在生产环境继续使用仓库中的示例密码和 token。

### Webhook 订阅

以下订阅只接收 `cycle.completed`，并要求批次上下文存在指定值。缺省从创建后的新事件开始；设置 `StartAfterIngestId: 0` 可以重放中心历史事件：

```bash
curl -X POST http://localhost:8000/api/v1/subscriptions \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "mes-cycle-consumer",
    "endpoint": "https://mes.example.com/hooks/ingot",
    "eventTypes": ["cycle.completed"],
    "context": {"material_lot": "LOT-001"},
    "secret": "replace-with-a-long-random-secret"
  }'
```

接收端应按 CloudEvent `id` 幂等处理。配置 `secret` 后，还应验证 `X-Ingot-Signature` 的 HMAC-SHA256。

### SQL 报表与完整性检查

仓库提供两个可直接交给 `psql` 的消费者脚本：

```bash
docker exec -i ingot-postgres psql -U ingot -d ingot \
  -v material_lot=LOT-001 < scripts/report-production-events.sql

docker exec -i ingot-postgres psql -U ingot -d ingot \
  -v edge_id=EDGE-001 < scripts/verify-event-integrity.sql
```

第一个脚本输出周期时长、配方、良品数和批次履历；第二个检查 `event_id`、`(edge_id, seq)` 唯一性、序号缺口以及只保留了幂等键却没有事实行的异常。

### 设备级

- v2 使用 `SourceCode`；v1 配置兼容 `PlcCode`
- `Driver`
- `Host`
- `Port`
- `ProtocolOptions`
- `Channels`
- `Asset`
- `Profile`
- `EventRules`

上线前建议执行：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

这是推荐流程的一部分，不是可选技巧。

## 上线后检查

系统启动后，先做这些检查。

### 1. 进程状态

- `Edge Agent` 是否在运行
- `InfluxDB` 是否可访问
- 如启用中心汇聚，PostgreSQL 与 `Central API` 是否健康

### 2. 健康接口

```bash
curl http://localhost:8001/health
```

### 3. 指标接口

```bash
curl http://localhost:8001/metrics
```

### 4. 日志状态

重点检查：

- 是否出现 PLC 连接错误
- 是否出现 TSDB 写入失败
- `event-log` 健康检查是否正常
- 是否出现事件持久化失败或积压指标
- 是否出现事件上行失败或边缘序号缺口指标
- 配置变更是否被正确加载

### 5. 存储写入

确认 InfluxDB 中已经有对应 measurement。

## 备份策略

如需保留诊断和条件采集上下文，至少备份这两类数据：

### 本地运行状态

- `Data/logs.db`
- `Data/acquisition-state.db`
- `Data/events.db`

如果需要更长的本地诊断窗口，应显式调大 `Logging:RetentionDays`，不要默认认为 `logs.db` 会无限增长。

### 存储

- InfluxDB bucket 数据
- 中心 PostgreSQL 数据库及其备份/WAL 策略

项目当前不依赖本地原始数据补偿目录，因此备份策略应以 InfluxDB 为主。

## 运维建议

- 使用 `systemd`、Windows Service 或其他服务管理器托管 `Edge Agent`
- 把中心服务和采集服务看作两个独立运行面
- 分别检查 `Edge -> InfluxDB` 遥测链路与 `Edge outbox -> Central PostgreSQL` 事件链路
- 如果 TSDB 写入失败，应把它视为需要立即处理的运行告警，而不是等待后台补写

## 相关文档

- [快速开始](tutorial-getting-started.md)
- [配置](tutorial-configuration.md)
- [驱动目录](hsl-drivers.md)
