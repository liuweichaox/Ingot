# 设计说明

Ingot 的核心产品是运行在车间侧的 `Edge Agent`。它不是一个单纯的 PLC 通讯库，而是一层轻量、边缘优先的生产事件基础设施：PLC 是首个数据源适配器，生产事件才是对上层稳定的产品模型。

## 1. 双平面

Ingot 同时保留两条语义不同的数据链路：

```text
Source
  |
  +-- telemetry --> ChannelCollector --> QueueService --> TSDB
  |
  `-- changes ----> EventRuleEvaluator --> EventSink --> events.db
                                                  |--> query / SSE
                                                  |--> TSDB projection
                                                  `--> EventShipper --> Central API --> PostgreSQL
```

遥测平面回答“当前值是多少”：

- 高频、低单位价值
- 在内存中批量聚合
- 默认写入 InfluxDB
- 保持 at-most-once：写入失败时记录日志和指标，丢弃当前批次

事件平面回答“发生了什么”：

- 低频、高业务价值
- 使用统一 `ProductionEvent` 信封
- 在任何投影前写入 SQLite `events.db`
- 落盘即表示事实成立
- 通过 `EventId`、边缘单调 `Seq` 和 `CorrelationId` 支持幂等、游标和周期关联

这不是两套重复实现。遥测适合实时趋势，事件适合产量、追溯、告警、MES 和 AI 消费。

## 2. 源与资产分离

`SourceCode` 标识通讯端点，`ObjectRef` 标识业务资产。

一台设备可能同时拥有 PLC、视觉和 CNC 数据源；一个 PLC 也可能服务多个业务资产。因此事件的 `Subject` 始终指向资产，PLC 地址、寄存器和驱动只存在于当前 PLC 适配路径。

SchemaVersion 2 使用：

- `SourceCode`
- `Adapter`
- `Profile`
- `Asset`
- `EventRules`

SchemaVersion 1 的 `PlcCode` 仍可加载，但只作为 `SourceCode` 的兼容别名。

## 3. 事件模型

生产事件由以下字段构成：

- `EventId`：UUIDv7 全局标识
- `EventType` / `EventTypeVersion`
- `OccurredAt` / `RecordedAt`
- `Source`
- `Subject`
- `Context`
- `Data`
- `CorrelationId`
- `Seq`

事件是不可变事实。TSDB 投影、SSE 和中心汇聚都是事实的下游视图，失败不能撤销已经落盘的事件。

当前事件来源包括：

- v1 `Conditional` 通道派生的 `cycle.started` / `cycle.completed`
- v2 `EventRules` 的 `EdgePair`
- 重启恢复产生的 `diagnostic.cycle_recovered` / `diagnostic.cycle_interrupted`
- PLC 写入成功产生的 `parameter.applied`

## 4. 上下文状态

`EdgeStateStore` 同时保存：

- active cycle
- 按资产键控的 `context_state`

上下文通过 `/api/v1/context/{subjectType}/{subjectId}` 写入和查询。事件规则声明 `ContextKeys`，事件产生时获取一次快照，使批次、工装、配方等业务信息与事实一起固化。

## 5. Profile

Profile 是最小行业语言声明，不是业务流程引擎。

当前提供：

- `profiles/core.json`
- `profiles/optical.json`

v2 配置加载时会验证：

- Profile 是否存在
- `Asset` / `Subject` 类型是否已声明
- 规则生成的事件类型是否已声明
- `ContextKeys` 是否覆盖事件要求的必需上下文

Profile 在进程启动时加载并固定，避免运行期间领域语言悄然漂移。

## 6. 存储与可靠性

本地 SQLite 文件职责明确：

| 文件 | 职责 |
|---|---|
| `events.db` | 不可变生产事件、边缘序号、待上行状态 |
| `acquisition-state.db` | active cycle 与业务上下文 |
| `logs.db` | 运行日志 |

事件日志启用 WAL 模式和索引，支持类型、主体、关联 ID、时间和序号查询。任意 `Context` 键值会在 append 同一事务中写入 `event_context(event_seq, ctx_key, ctx_value)`，查询通过 `(ctx_key, ctx_value, event_seq)` 索引执行，不依赖预先知道行业字段。旧数据库启动时会从 `context_json` 自动补建索引。

事件持久化失败会记录最近错误和连续失败次数，并把 `/health` 置为降级；下一次 append 成功后自动恢复。可选 Influx 投影失败只记录警告，不影响事实成立。

`EventShipper` 按边缘 `Seq` 读取待上行事件，批次最多 500 条；网络或中心失败时指数退避并安全重发，只有收到合法 `AckSeq` 后才标记本地事件已上行。Central 通过 `EventId` 与 `(EdgeId, Seq)` 双重唯一约束实现幂等摄入，因此传输语义是 at-least-once，中心存储结果是 exactly-once effect。

中心事件库使用 PostgreSQL：

- `production_events` 按 `OccurredAt` 月度分区
- `Context` / `Data` 使用 JSONB，事件上下文建立 GIN 索引
- 独立 `IngestId` 作为中心 SSE 游标
- per-edge bearer token 保护批量摄入端点
- 落库前验证 UUIDv7、事件类型/版本、主体、上下文、正序号、批内双重唯一性，以及 `Source` 与 token 所属 Edge 的一致性
- 检测并暴露边缘序号缺口

Webhook 订阅同样持久化在 PostgreSQL。每个订阅拥有独立中心游标，未匹配事件也会推进游标；匹配事件仅在接收端返回 2xx 后推进，因此失败会安全重试。对外载荷使用 CloudEvents 1.0 structured 模式，`EventId` 映射为 `id`，并可使用订阅密钥生成 `X-Ingot-Signature: sha256=...`。

性能门由 `scripts/benchmark-edge-event-log.sh` 和 `scripts/benchmark-central-ingest.sh` 提供，分别覆盖百万行本地查询/落盘延迟与真实 PostgreSQL HTTP 摄入吞吐、序号完整性。

## 7. 配置与运行时

配置使用 JSON 文件并支持热更新。启动前可运行：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs --config-dir ./examples/device-configs
```

配置先经过严格结构与运行时校验，再经过 Profile 校验。未知 JSON 字段（包括拼错的键）、重复 `SourceCode`、无效驱动参数、重复规则、未知对象/事件类型等都会被拒绝。

## 8. API 边界

Edge Agent 当前提供：

- `GET /health`
- `GET /metrics`
- `GET /api/v1/events`
- `GET /api/v1/events/stream`
- `GET /api/v1/cycles/{correlationId}`
- `GET|PUT /api/v1/context/{subjectType}/{subjectId}`
- `GET /api/acquisition/plc-connections`
- `POST /api/acquisition`

事件查询支持类型、主体、关联 ID、时间窗、`afterSeq` 和 `ctx.*` 过滤。Edge 与 Central 复用同一查询边界校验：反向时间窗、负游标、越界 limit、非法上下文键和非法 `Last-Event-ID` 都返回 400。SSE 使用 `Last-Event-ID` 继续游标。

Central API 提供：

- `POST /api/v1/events:batch`
- `GET /api/v1/events`
- `GET /api/v1/events/stream`
- `GET /api/v1/cycles/{correlationId}`
- `POST|GET /api/v1/subscriptions`
- `GET|DELETE /api/v1/subscriptions/{subscriptionId}`
- `PUT /api/v1/subscriptions/{subscriptionId}/enabled`

中心查询额外支持 `edgeId`，SSE 使用中心 `IngestId` 续读。

## 9. 分层原则

- `Domain`：源配置、事件、Profile 和基础领域模型
- `Application`：运行时抽象与契约
- `Infrastructure`：采集、驱动、SQLite、InfluxDB、Profile、指标实现
- `Edge.Agent`：依赖注入、宿主、API 与后台服务
- `Central.Api`：PostgreSQL 事件枢纽、节点注册与诊断代理
- `Central.Web`：节点、指标、日志和生产事件视图

长期坚持：

- Edge First
- Source Neutral Above Adapters
- Facts Before Projections
- Configuration Before Runtime
- Explicit Failure Semantics
- UTC Everywhere

更完整的演进范围、取舍和阶段验收见 [生产事件 RFC](rfc-production-events.md)。
