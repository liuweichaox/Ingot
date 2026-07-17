# RFC: Ingot — 从 PLC 采集到生产事件基础设施

| | |
|---|---|
| 状态 | Draft v0.3（v0.2：源中立抽象、资产/来源分离；v0.3：产品定名 Ingot，命名释义见 §12） |
| 日期 | 2026-07-16 |
| 范围 | Ingot（原 DataAcquisition）项目的中长期架构演进 |
| 讨论 | GitHub Issues / Discussions |

---

## 0. 摘要

本 RFC 提议把项目（更名为 **Ingot**，原 DataAcquisition）的定位从「PLC 数据采集运行时」演进为「**生产事件基础设施**」，并为更远的「工业数据平台」留出空间。

一句话定位：

> 不是采集 PLC，而是采集生产事件。PLC 只是第一个、也是目前最强的一种源适配器；生产事件模型才是产品。

产品名 **Ingot（锭）** 就是这个定位的隐喻：遥测是矿砂，Ingot 把它熔炼成标准化、一旦铸成即不可变、可堆叠储存的"事实之锭"（命名释义见 §12）。

具体做法是在现有采集链路之上引入一个独立的**事件平面**，并把上层模型建立在源中立的抽象上：

- 底层：**源适配层**——PLC 通讯与采集是首个适配器（现状实现保持不动）；OPC UA、CNC、视觉、SCADA/DCS 是预留席位
- 中层：数据标准化、**事件化**（本 RFC 核心）、时序与事件双存储
- 上层：MES、报表、追溯、AI —— 全部作为事件的消费者

事件被定义为不可变的业务事实五元组：**(类型, 时间, 主体, 上下文, 载荷)**。遥测回答"现在值是多少"，事件回答"发生了什么"。事件的主体是**资产**（设备/产线），不是通讯端点——上层模型里没有 PLC 这个词。

演进遵循一条纪律：**先长出来，再抽象**。先用「光学行业 × PLC 源」跑通闭环，共性重复出现之后才沉淀进核心模型；适配器接口同理，第二个适配器落地时才抽取。

---

## 1. 动机与定位

### 1.1 为什么不做"又一个 PLC 通讯库"

通讯库赛道已经成熟：HslCommunication、S7NetPlus、libplctag 等项目覆盖了主流协议。本项目自己的架构已经承认了这一点——`Driver` 是稳定名称，Hsl 只是 `IPlcDriverProvider` 的默认实现，"不是架构前提"（见 `docs/design.md` 第 5 节）。

**通讯层在本项目里从来不是护城河，它是采购件。** 把精力继续投在协议覆盖上，是在别人的主场竞争。

更根本的是：**工业不等于 PLC**。车间里还有 OPC UA 服务器、工业机器人、CNC、视觉系统、SCADA、DCS；行业间差异更大——离散制造 PLC 密集，流程工业的主角是 DCS 与专用系统。把平台概念绑死在 PLC 上，天花板就只是离散制造的一个切面。因此本设计抽象的是**数据来源（Source）**，PLC 只是其中轮询范式下的第一个适配器（§3.4）。

### 1.2 价值主张的四级迁移

```text
寄存器值          遥测点            生产事件              生产语言
D6006 = 1   →   sensor.temp=63.2  →  cycle.completed     →  行业 Profile
                                      {lot, tooling,          (可配置领域模型)
                                       recipe, duration}
─────────────    ──────────────    ─────────────────     ─────────────────
通讯库的领域      当前项目所在层     本 RFC 的目标          长期沉淀
(红海)           (有同类)          (开源 .NET 生态空位)    (难以复刻)
```

每往右一级，数据离业务更近一步，可替代性下降一级：

- 寄存器值：任何通讯库都能给
- 遥测点：Telegraf、Node-RED、各类网关都能给
- **生产事件：带上下文的业务事实，需要领域建模 + 现场打磨的规则库，这是空位**
- 生产语言：行业通用事件模型，网络效应所在，但必须从上一级长出来

### 1.3 生态卡位

| 层次 | 代表项目 | 与本项目的关系 |
|---|---|---|
| 通讯库 | HslCommunication、S7NetPlus、libplctag | 本项目的下游依赖，不竞争 |
| 通用采集器 | Telegraf、Node-RED、FUXA | 只做"点位→存储"，无业务事件模型 |
| SCADA / IoT 平台 | Ignition、ThingsBoard | 重平台，闭环生态，部署与心智成本高 |
| 工业 DataOps | HighByte Intelligence Hub | 商业闭源，模型化思路可参照 |
| UNS / 制造数据平台 | United Manufacturing Hub（Kafka/TimescaleDB 栈）、Rhize（ISA-95 GraphQL 栈） | 思路同源，但都是重基建（K8s、Kafka、GraphQL 数据库）；**轻量、边缘优先、.NET 生态的生产事件层没有开源占位者** |

本项目的卡位：**单二进制起步、边缘优先、配置驱动的开源生产事件层**。UMH 们证明了"事件化 + 统一命名"这个方向的商业价值，同时它们的部署重量恰好留出了轻量位。

### 1.4 为什么现有代码离这一步不远

这是本 RFC 最重要的工程判断——事件化不是推倒重来，是把已有的雏形扶正：

| 现有实现 | 事件化视角下的本质 |
|---|---|
| `DataMessage.EventType`（Start/End/Data）+ `CycleId` | 已经是原始的周期事件对，只是被当作"带标记的遥测点"写进了 InfluxDB |
| `ConditionalAcquisition`（RisingEdge/FallingEdge） | 已经是事件触发器，只是种类只有"边沿对"一种 |
| `AcquisitionStateManager` + `acquisition-state.db` | 已经是边缘状态存储，只差从"活跃周期"泛化为"上下文状态"（当前批次/当前模具/当前配方） |
| `DiagnosticEventType`（RecoveredStart/Interrupted）与 `_diagnostic` measurement 隔离 | 已经是"正式事实与诊断事实分离"的事件治理雏形 |
| `Contracts/PlcWriteRequest`（中心下发写 PLC） | 参数下发事件（parameter.applied）的天然产生点 |

缺的不是采集能力，是**三件事**：事件的一等公民地位（独立信封与存储）、上下文机制（事件发生时自动携带批次/工装/配方）、以及与丢弃语义分离的**持久化保障**。

---

## 2. 需求

### 2.1 功能性需求

| # | 需求 | 说明 |
|---|---|---|
| FR1 | 事件信封 | 统一的生产事件结构：类型、时间、主体、上下文、载荷、关联 ID |
| FR2 | 事件派生规则 | 配置驱动地把寄存器变化翻译成事件（边沿对、值变更、位标志、阈值） |
| FR3 | 上下文状态 | 边缘侧维护每设备的业务上下文（当前批次、工装、配方、操作员），事件发出时自动快照 |
| FR4 | 事件持久化 | 事件写入本地不可变日志，进程重启、断网不丢失 |
| FR5 | 事件查询与订阅 | 边缘本地查询 API + 流式订阅；中心侧聚合查询 |
| FR6 | 中心汇聚 | 多边缘事件汇聚到中心事件库，幂等去重，断点续传 |
| FR7 | 行业 Profile | 对象类型与事件类型由 Profile 声明，配置校验时检查引用合法性 |
| FR8 | 向后兼容 | SchemaVersion 1 配置零改动继续工作；现有遥测链路行为不变 |
| FR9 | 追溯查询 | 按批次 / 设备 / 周期 / 时间窗还原事件链（"这批料经过哪些设备、什么参数、有无报警"） |
| FR10 | 源中立模型 | 事件模型、上下文、API 与存储不出现 PLC 专有概念；寄存器/协议语汇只存在于适配器内部 |
| FR11 | 源与资产分离 | 通讯端点（Source）与业务对象（Asset）解耦：一台设备可有多个源（PLC+视觉+CNC），一个 PLC 也可服务多台设备 |

### 2.2 非功能性需求

| # | 需求 | 指标 |
|---|---|---|
| NFR1 | 边缘资源占用 | 事件平面新增内存 < 50MB，磁盘由保留策略约束 |
| NFR2 | 事件落地延迟 | 触发条件成立 → 本地持久化 < 50ms（P99） |
| NFR3 | 事件可靠性 | at-least-once：落盘即不丢；消费端按 EventId 幂等 |
| NFR4 | 遥测可靠性 | 维持现状 at-most-once（**显式不改**，见 §3.2） |
| NFR5 | 本地查询 | 100 万行事件表上常见过滤查询 P95 < 100ms |
| NFR6 | 中心吞吐 | 单中心节点 ≥ 500 events/s 摄入（远超预估负载，见 §6.1） |
| NFR7 | 断网容忍 | 边缘断网期间事件继续本地累积，恢复后自动补传，无缺口无重复 |

### 2.3 约束

- 小团队开源项目：每阶段必须独立可交付、可演示，不允许"半年不见产出"的大重构
- .NET 单二进制起步：Phase 1–2 不允许引入除 SQLite 外的新基础设施依赖
- 现有用户不破坏：遥测链路、v1 配置、现有端点全部保持
- 文档只承诺真实支持的能力（延续项目现有原则）

### 2.4 非目标

明确不做，避免范围失控：

- 不做全功能 MES（工单、排产、人员）——那是事件的**消费者**
- 不做 SCADA/HMI 组态画面
- 不追求 exactly-once（成本与收益不匹配，见 §9 D7）
- Phase 1–3 不引入消息中间件（Kafka/MQTT broker/NATS）
- Phase 1–3 不实现第二个源适配器（OPC UA 等）——但**命名与契约从现在起源中立**（§3.4、D10）
- 不做多租户 SaaS 化
- 不在早期为"所有行业"建模——只服务光学场景，共性靠事后提炼

---

## 3. 总体架构

### 3.1 分层模型

```text
┌────────────────────────────────────────────────────────────────┐
│ L4 消费层        MES │ 报表/BI │ 追溯 │ AI/异常检测 │ 告警联动    │
│                  （全部是事件与遥测的消费者，不属于本项目内核）      │
├────────────────────────────────────────────────────────────────┤
│ L3 服务层        事件查询 API │ 流式订阅(SSE) │ 中心事件库(PG)     │
│                  遥测查询(TSDB) │ Central Web 事件流视图           │
├────────────────────────────────────────────────────────────────┤
│ L2 事件层 ★      事件派生规则(EventRules) │ 上下文状态(Context)    │
│                  事件信封(ProductionEvent) │ 本地事件日志(outbox)  │
├────────────────────────────────────────────────────────────────┤
│ L1 采集层        ChannelCollector │ 心跳门控 │ 表达式求值           │
│                  批量聚合(QueueService) → TSDB   （现状，不动）    │
├────────────────────────────────────────────────────────────────┤
│ L0 源适配层      Source Adapters                                 │
│                  PLC＝首个适配器（现状：IPlcDriverProvider /       │
│                  IPlcClientService / Hsl / Driver 名称选择，不动） │
│                  OPC UA │ CNC │ 视觉 │ SCADA/DCS（预留席位）       │
└────────────────────────────────────────────────────────────────┘
```

★ L2 是本 RFC 的全部增量。L0/L1 现有实现不动（PLC 是首个适配器），L3 在 Phase 3 落地，L4 永远在项目边界外。

### 3.2 核心架构决策：双平面

这是整个设计中最重要的一条决策，它同时回答"怎么加可靠性"和"怎么不破坏现状"：

| | 遥测平面（现状） | 事件平面（新增） |
|---|---|---|
| 回答的问题 | 现在值是多少 | 发生了什么 |
| 数据形态 | `DataMessage`，高频采样 | `ProductionEvent`，低频业务事实 |
| 典型速率 | 10²–10⁴ 点/秒/边缘 | 10⁻¹–10⁰ 事件/秒/边缘 |
| 可靠性语义 | **at-most-once**：写 TSDB 失败即丢弃（现状不变） | **at-least-once**：先落本地日志，再分发 |
| 存储 | InfluxDB（可替换） | 边缘 SQLite 事件日志 → 中心 PostgreSQL |
| 可重建性 | 丢了就丢了（可接受） | 业务事实，不可丢；投影可重建 |

理由：`docs/design.md` 把"不做本地 WAL"作为显式取舍，这个取舍对高频遥测依然成立——为每秒几千个采样点做持久化补偿，复杂度不划算。但**事件是另一种数据**：一台设备一个周期 30–120 秒，事件速率比遥测低三个数量级，持久化成本趋近于零；而一条 `cycle.completed` 丢失意味着产量统计错误、追溯断链。**低频高价值数据用高保障，高频低价值数据用低保障**——分级而不是一刀切，才能既守住"Real-Time First"又支撑业务事实。

### 3.3 运行时数据流

```text
                        Edge Agent（单进程）
  ┌──────────────────────────────────────────────────────────────┐
  │                                                              │
  │  PLC ──读取──► ChannelCollector ──┬─► DataMessage ─► Queue ──┼─► InfluxDB
  │                （心跳门控，现状）    │   (遥测平面，不动)  Service │   (at-most-once)
  │                                   │                          │
  │                                   ▼                          │
  │                          EventRuleEvaluator ★                │
  │                          （边沿对/值变更/位标志/阈值）           │
  │                                   │                          │
  │                     ┌─────────────┤                          │
  │                     ▼             ▼                          │
  │              EdgeContextStore   ProductionEvent              │
  │              (当前批次/工装/配方)   (信封+上下文快照)              │
  │                     │             │                          │
  │                     │             ▼                          │
  │                     │      EventLog (SQLite outbox) ★        │
  │                     │      落盘即"已发生"，不可变                │
  │                     │             │                          │
  │                     │      ┌──────┴────────┐                 │
  │                     │      ▼               ▼                 │
  │                     │  TSDB 投影        EventShipper ★       │
  │                     │  (可选,可重建)     (批量上行,重试,断点)     │
  │                     │                      │                 │
  └─────────────────────┼──────────────────────┼─────────────────┘
                        │                      ▼
                        │               Central API ★
                        │               ingest(幂等) ─► PostgreSQL
                        │                      │        事件库
                        │                      ▼
                        │               查询 API / SSE / Webhook
                        │                      │
                        └── 本地查询/SSE        ▼
                            (边缘自治)      MES / 报表 / AI / 追溯
```

★ 为新增组件。关键性质：

- 事件在**边缘落盘即算发生**，上行只是复制——断网不影响事件的产生与本地消费
- TSDB 投影是**派生数据**：把事件同时写一份到 InfluxDB（沿用现有 `QueueService`），现有 Grafana 面板继续可用；丢了可从事件日志重建
- 中心事件库是**多边缘的汇聚视图**，不是事实源；事实源永远是各边缘的本地日志
- 图中 PLC 的位置泛化为"数据源"：事件层消费的是归一化标签流与规则求值结果，不感知协议（§3.4）

### 3.4 源适配层：范式归一

不同源的差异不在协议名，在**交互范式**。适配层把四种范式归一为同一种产物——标签样本流：

| 范式 | 典型源 | 归一方式 |
|---|---|---|
| 轮询读 | PLC、Modbus 设备 | 适配器内部轮询产出样本流（现状实现即此类） |
| 订阅/浏览 | OPC UA、部分 SCADA/DCS 网关 | 服务端订阅直接透传为样本流，携带源时间戳与质量码 |
| 事件推送 | 视觉系统、机器人控制器消息 | 天然事件形态，经映射直接进事件管线（未来触发器种类 `SourceEvent`） |
| 文件/批 | CNC 加工记录、离线质检 | 目录/接口监听转样本或事件（远期，按需求评估） |

```csharp
/// <summary>归一化标签样本：事件规则的源中立输入。</summary>
public sealed record TagSample(
    string SourceCode,            // 源编码（v1 的 PlcCode 是其别名）
    string Tag,                   // 适配器原生地址：PLC 寄存器 "D6006"、OPC UA NodeId…
    object? Value,
    DateTimeOffset ObservedAt,    // 采集侧观察时间（UTC）
    DateTimeOffset? SourceTime,   // 源侧时间戳（OPC UA 等提供；PLC 轮询为 null）
    TagQuality Quality);          // Good / Uncertain / Bad（轮询成功默认 Good）
```

抽象的纪律（与 Profile 同一条规则）：

- **命名与契约现在就中立**：v2 配置、事件模型、API 中不出现 PLC 专有词——v2 尚未发布，此刻改名成本为零，日后为负担
- **接口抽取等第二个适配器**：现有 `IPlcClientService` 体系原样作为 PLC 适配器的内部实现；`ISourceAdapter` 接口在 OPC UA 适配器立项时才抽取（rule of two，避免对着想象设计接口）
- **遥测通道暂不收敛**：`Channels[]` 保持现有读取路径与适配器专有配置；向统一样本流收敛是适配器轨道（§8）的工作，不阻塞事件层

---

## 4. 核心模型

### 4.1 事件信封 ProductionEvent

Domain 层新增（`src/DataAcquisition.Domain/Events/`）：

```csharp
/// <summary>生产事件：不可变的业务事实。</summary>
public sealed record ProductionEvent
{
    /// <summary>全局唯一，UUIDv7（Guid.CreateVersion7()，时间有序，无新依赖）。</summary>
    public required string EventId { get; init; }

    /// <summary>事件类型，形如 "cycle.completed"。见 §4.2。</summary>
    public required string EventType { get; init; }

    /// <summary>事件类型的载荷结构版本。</summary>
    public int EventTypeVersion { get; init; } = 1;

    /// <summary>业务发生时间：采集侧观察到触发条件成立的 UTC 时间。</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>边缘落盘时间（UTC）。与 OccurredAt 的差即处理延迟。</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>事件来源，"edge/{edgeId}/{sourceCode}/{ruleId}"。</summary>
    public required string Source { get; init; }

    /// <summary>事件主体：这件事发生在谁身上。</summary>
    public required ObjectRef Subject { get; init; }

    /// <summary>上下文快照：事件发生那一刻的业务环境（批次、工装、配方…）。</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();

    /// <summary>载荷：本事件特有的数据（时长、良品数、报警码…）。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>成对/成组事件的关联 ID（周期、报警实例）。沿用现有 CycleId 语义。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>边缘单调序号（SQLite 自增），消费端按 (EdgeId, Seq) 定序与查缺口。</summary>
    public long Seq { get; init; }
}

/// <summary>对象引用：类型 + 标识。类型由 Profile 声明，核心不硬编码。</summary>
public sealed record ObjectRef(string Type, string Id);
```

设计要点：

- **五元组完整**：EventType（什么事）、OccurredAt（何时）、Subject（谁身上）、Context（什么环境下）、Data（细节）——这正是"每个事件都有时间、对象、上下文"的落地
- **上下文是快照不是引用**：`Context` 存的是事件发生那一刻的值（如 `"material_lot": "L20260716-01"`），不是外键。事后改主数据不会篡改历史事实，这是追溯可信的前提
- **时间诚实**：`OccurredAt` 是采集侧观察时间，不假装是 PLC 内部时间。若 PLC 寄存器里有设备侧时间戳，作为 `Data` 字段快照进来，不冒充信封时间
- **与 CloudEvents 对齐而非内嵌**：内部模型保持扁平高效，对外集成时序列化为 CloudEvents 1.0 JSON——`EventId→id`、`EventType→type`（加前缀成反向域名）、`Source→source`、`OccurredAt→time`、`Subject.Id→subject`、`Data+Context→data`。收益是 webhook 消费者、云端事件网关可以零适配接入

### 4.2 事件类型体系（core taxonomy v1）

命名规则：`<类别>.<动词过去式>`，小写点分。刻意只有 **6 个正式类别 + 1 个诊断类别**：

| 类别 | 事件类型 | 语义 | 光学场景示例 |
|---|---|---|---|
| cycle | `cycle.started` / `cycle.completed` / `cycle.aborted` | 一次加工/工艺周期 | 一模镜片的抛光周期 |
| state | `state.changed` | 设备状态迁移（词汇参考 PackML，不强制） | 待机→运行→故障 |
| alarm | `alarm.raised` / `alarm.cleared` | 报警发生与恢复（成对） | 主轴过载报警 |
| parameter | `parameter.applied` | 配方/参数下发生效 | 镀膜配方切换为 R-AR-v3 |
| material | `material.lot_changed` / `material.consumed` | 物料批次变更/消耗 | 毛坯批次切换 |
| tooling | `tooling.changed` | 工装/模具/夹具切换 | 抛光模更换 |
| diagnostic | `diagnostic.cycle_recovered` / `diagnostic.cycle_interrupted` / `diagnostic.backlog_dropped` | 恢复/中断/降级审计 | 进程重启后的周期恢复 |

治理规则：

1. **正式事实与诊断事实分离**——直接继承现有 `DiagnosticEventType` 与 `_diagnostic` measurement 的设计哲学：正式统计只基于正式类别，诊断类别只用于审计与排障
2. **模具不是一等概念**：`tooling` 才是。"模具"是光学 Profile 给 `tooling` 类型对象起的显示名。没有模具的行业禁用或重命名该类别——这就是"抽象事件、对象和关系，而不是行业名词"
3. **新增类别的门槛**（先长出来再抽象的机制化）：一个候选概念必须在 **两个以上真实场景** 反复出现，才允许进入 core taxonomy；此前一律放在 `Data` 载荷里长着
4. 自定义事件类型允许以 `x.` 前缀存在（如 `x.optical.coating_drift`），Profile 内合法，不进 core

### 4.3 资产、来源与上下文

**对象类型不硬编码。** 核心只认识 `ObjectRef(Type, Id)`，合法的 `Type` 集合由 Profile 声明（§4.5）。

**资产（Asset）与来源（Source）是两个维度**，这是源中立最关键的一刀：

- Source 是通讯端点：某个 PLC、某台 OPC UA 服务器、某个视觉工位的推送口
- Asset 是业务对象：事件的 `Subject`、上下文的归属者
- 关系是多对多：一台设备可同时有 PLC + 视觉 + CNC 三个源；一个 PLC 也可服务一条线上多台设备
- Phase 2 的 PLC 单源现场里两者常常 1:1（规则未声明 `Subject` 时缺省取源编码），但模型从第一天就分开——将来视觉源的判定事件能自动携带 PLC 规则写入的批次上下文，正是因为上下文挂在资产而不是源上

**EdgeContextStore**：`AcquisitionStateManager` 的直接泛化。当前它只存"活跃周期"，泛化后按**资产**存任意业务上下文键值：

```sql
-- 与 acquisition-state.db 同库或独立 events.db，延续现有 SQLite + WAL 模式
CREATE TABLE context_state (
  asset_type  TEXT NOT NULL,   -- 如 equipment / line
  asset_id    TEXT NOT NULL,
  ctx_key     TEXT NOT NULL,   -- 如 material_lot / tooling / recipe / operator
  ctx_value   TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  PRIMARY KEY (asset_type, asset_id, ctx_key)
);
```

工作方式：

1. 上下文由事件规则的 `SetContext` 效果写入（如"批次寄存器值变更 → 更新 `material_lot` 并发出 `material.lot_changed`"）
2. 任何事件发出时，按规则声明的 `ContextKeys` 从状态库取当前值快照进 `Context`
3. 内存热缓存 + SQLite 镜像，重启自动恢复——与现有 active cycle 恢复完全同构

这一步是"数据天然可追溯"的机关所在：**上下文在采集侧自动缝合**。周期事件不需要 MES 事后关联批次——它出生时就带着批次。

### 4.4 关联与追溯

三种关联手段，各司其职：

| 手段 | 机制 | 用途 |
|---|---|---|
| CorrelationId | 成对事件共享 ID（started/completed、raised/cleared），沿用现有 CycleId 机制（`AcquisitionStateManager.StartCycle` 生成，End 取回） | 把"对"拼成"段"：周期时长、报警持续时间 |
| Context 键 | 事件携带的上下文快照 | 跨设备、跨时间的业务线索串联 |
| (EdgeId, Seq) | 边缘单调序号 | 技术定序、断点续传、缺口检测 |

追溯即查询，不需要专门的"追溯模块"：

```sql
-- 光学场景：批次 L20260716-01 的完整生产履历
SELECT occurred_at, event_type, subject_id, data
FROM   production_events
WHERE  context->>'material_lot' = 'L20260716-01'
ORDER  BY occurred_at;
-- 返回：几点换的批次 → 在哪些设备上跑了哪些周期(什么配方/模具) → 有无报警 → 何时恢复
```

这条查询就是 Phase 2 验收演示，也是向 MES/AI 讲"同一套语言"的最短证明。

### 4.5 行业 Profile：可配置的领域模型

Profile 回答"这个行业里有哪些对象、说哪些事件、事件里必须带什么"：

```text
profiles/
├── core/profile.json        # 内置：6+1 类别、最小对象类型集
└── optical/profile.json     # 光学行业包，扩展 core
```

```jsonc
// profiles/optical/profile.json（示意）
{
  "SchemaVersion": 1,
  "Profile": "optical",
  "Extends": "core",
  "ObjectTypes": {
    "equipment":    { "DisplayName": "设备" },
    "tooling":      { "DisplayName": "模具/工装",
                      "Attributes": { "mold_class": "string" } },
    "material_lot": { "DisplayName": "毛坯批次" },
    "recipe":       { "DisplayName": "工艺配方" }
  },
  "EventCategories": {
    "cycle":     { "Enabled": true,
                   "RequiredContext": ["material_lot", "tooling"],
                   "DataSchema": { "recipe_id": "string", "good_count": "int" } },
    "tooling":   { "Enabled": true },
    "material":  { "Enabled": true },
    "alarm":     { "Enabled": true },
    "parameter": { "Enabled": true },
    "state":     { "Enabled": false }          // 光学场景暂不用，关掉
  },
  "CustomEventTypes": {
    "x.optical.coating_drift": { "DataSchema": { "drift_nm": "double" } }
  }
}
```

约束与机制：

- 源配置里的 `EventRules` 引用的对象类型、事件类别必须在生效 Profile 中声明且启用，否则 `--validate-configs` 失败——**"配置先校验，再运行"原则原样延伸到领域模型**
- `RequiredContext` 声明该行业里某类事件必须携带的上下文；缺失时事件照发但打诊断标记（现场经常不完美，拒发比缺字段更糟）
- 没有某概念的行业：`Enabled: false` 关闭类别，或重命名 `DisplayName`。**底层事件模型统一，行业差异全部在 Profile 配置层吸收**
- 纪律：Phase 2 只实现校验所需的最小机制（类型声明 + 启用开关 + 必填上下文）。属性 schema 校验、Profile 继承链、版本仲裁等，等第二个行业 Profile 出现时再做——机制本身也遵守"先长出来再抽象"

### 4.6 事件派生规则 EventRules（配置 SchemaVersion 2）

源配置（原"设备配置"）新增 `EventRules[]`，与 `Channels[]`（遥测）并列。规则引用**标签（Tag）**而非寄存器——寄存器只是 PLC 适配器的标签语法。触发器四种起步：

| Kind | 语义 | 产出 | 现有基础 |
|---|---|---|---|
| `EdgePair` | 标签值边沿对（起/止） | `*.started` / `*.completed` 成对事件 | 就是现有 `ConditionalAcquisition`，直接泛化 |
| `ValueChanged` | 标签值变更 | 单事件 + 可选 `SetContext` | 新增，读值比较即可 |
| `BitFlag` | 位 0→1 / 1→0 | `alarm.raised` / `alarm.cleared` 成对 | EdgePair 的位变体，复用配对状态机 |
| `Threshold` | 进入/离开数值区间 | 成对事件 | 表达式基础可复用（MetricExpressionEvaluator 的求值能力） |

光学抛光机完整示例：

```jsonc
{
  "SchemaVersion": 2,
  "IsEnabled": true,
  "SourceCode": "POL-03-PLC",   // v2 更名；v1 的 PlcCode 作为永久别名兼容
  "Adapter": "plc",             // 适配器类型，缺省 "plc"；未来 "opcua" 等
  "Driver": "melsec-mc",
  "Host": "192.168.10.33", "Port": 6000,
  "HeartbeatMonitorRegister": "D100",
  "HeartbeatPollingInterval": 5000,
  "Profile": "optical",

  "Channels": [ /* 遥测通道，v1 结构原样保留（适配器专有配置） */ ],

  "EventRules": [
    {
      "RuleId": "polish-cycle",
      "Category": "cycle",
      "Subject": { "Type": "equipment", "Id": "POL-03" },   // 资产，不是通讯端点
      "Trigger": { "Kind": "EdgePair", "Tag": "D6006", "DataType": "short",
                   "Start": "RisingEdge", "End": "FallingEdge" },
      "SnapshotOnStart": [
        { "FieldName": "recipe_id", "Tag": "D6100", "DataType": "string",
          "StringByteLength": 16 }
      ],
      "SnapshotOnEnd": [
        { "FieldName": "good_count", "Tag": "D6110", "DataType": "short" }
      ],
      "ContextKeys": [ "material_lot", "tooling" ]
    },
    {
      "RuleId": "lot-change",
      "Category": "material",
      "EventType": "material.lot_changed",
      "Subject": { "Type": "equipment", "Id": "POL-03" },
      "Trigger": { "Kind": "ValueChanged", "Tag": "D6200",
                   "DataType": "string", "StringByteLength": 20 },
      "SetContext": { "material_lot": "$value" }
    },
    {
      "RuleId": "mold-change",
      "Category": "tooling",
      "EventType": "tooling.changed",
      "Subject": { "Type": "equipment", "Id": "POL-03" },
      "Trigger": { "Kind": "ValueChanged", "Tag": "D6300",
                   "DataType": "string", "StringByteLength": 12 },
      "SetContext": { "tooling": "$value" }
    },
    {
      "RuleId": "spindle-alarm",
      "Category": "alarm",
      "Subject": { "Type": "equipment", "Id": "POL-03" },
      "Trigger": { "Kind": "BitFlag", "Tag": "M100", "Bit": 0 },
      "Data": { "alarm_code": "SPINDLE_OVERLOAD" }
    }
  ]
}
```

语义细则：

- 规则求值运行在现有采集循环模式内：同样受心跳门控（PLC 未连接不求值）、同样的读取器与批量读缓冲复用
- `EdgePair` 周期语义完全继承现状：End 先于 Start 处理（同时触发时先结束旧周期）、首采样恢复判定产生 `diagnostic.*` 事件——`ChannelCollector.HandleRecoveryOnFirstSampleAsync` 的逻辑原样迁移
- `SetContext` 与事件发出是同一原子动作：先更新上下文再发事件，事件自身的 Context 反映**变更后**的值
- **v1 → v2 自动映射**：加载器把 v1 的 `AcquisitionMode: Conditional` 通道翻译为一条隐式 cycle 规则（`Metrics` → `SnapshotOnStart`）；`PlcCode` / `Register` 作为 `SourceCode` / `Tag` 的永久别名。事件平面对老配置同样生效，v1 配置文件本身永不要求改动

---

## 5. 存储与接口

### 5.1 边缘事件日志（events.db，outbox 模式）

```sql
-- SQLite，WAL + synchronous=NORMAL（与现有状态库同参数）
CREATE TABLE events (
  seq           INTEGER PRIMARY KEY AUTOINCREMENT,  -- 边缘单调序号
  event_id      TEXT NOT NULL UNIQUE,               -- UUIDv7
  event_type    TEXT NOT NULL,
  type_version  INTEGER NOT NULL DEFAULT 1,
  occurred_at   TEXT NOT NULL,                      -- ISO-8601 UTC
  recorded_at   TEXT NOT NULL,
  source        TEXT NOT NULL,
  subject_type  TEXT NOT NULL,
  subject_id    TEXT NOT NULL,
  correlation_id TEXT,
  context_json  TEXT NOT NULL DEFAULT '{}',
  data_json     TEXT NOT NULL DEFAULT '{}',
  ship_state    INTEGER NOT NULL DEFAULT 0,         -- 0=待上行 1=已确认
  ship_attempts INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_events_type_time    ON events(event_type, occurred_at);
CREATE INDEX idx_events_subject_time ON events(subject_type, subject_id, occurred_at);
CREATE INDEX idx_events_ship         ON events(ship_state, seq);
```

写入路径与保留策略：

- 发出事件 = **同步 append 到本表**（单行 INSERT，WAL 下亚毫秒级），落盘即算"已发生"；之后的 TSDB 投影、上行分发全部异步，失败不影响事实成立
- 已确认上行的事件按 `Events:RetentionDays`（默认 7 天）清理，沿用日志清理的既有配置风格
- 未上行事件不清理，直到触及硬上限 `Events:MaxBacklogRows`（默认 50 万行）；触及后丢最旧并发出 `diagnostic.backlog_dropped` + 指标告警——**极端情况的丢弃是显式的、可观测的**，延续项目"暴露失败而不是隐藏失败"的原则

### 5.2 中心事件库（PostgreSQL，Phase 3）

事件是半结构化业务事实，要按上下文键任意过滤、与主数据关联——这是关系库 + JSONB 的主场，不是 TSDB 的：

```sql
CREATE TABLE production_events (
  event_id      TEXT PRIMARY KEY,                   -- 天然幂等键
  edge_id       TEXT NOT NULL,
  seq           BIGINT NOT NULL,
  event_type    TEXT NOT NULL,
  type_version  INTEGER NOT NULL,
  occurred_at   TIMESTAMPTZ NOT NULL,
  recorded_at   TIMESTAMPTZ NOT NULL,
  ingested_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  subject_type  TEXT NOT NULL,
  subject_id    TEXT NOT NULL,
  correlation_id TEXT,
  context       JSONB NOT NULL DEFAULT '{}',
  data          JSONB NOT NULL DEFAULT '{}',
  UNIQUE (edge_id, seq)                             -- 缺口检测
) PARTITION BY RANGE (occurred_at);                 -- 按月分区

CREATE INDEX ON production_events (event_type, occurred_at);
CREATE INDEX ON production_events (subject_type, subject_id, occurred_at);
CREATE INDEX ON production_events USING GIN (context);
```

事件同时可选投影到 InfluxDB（`events` measurement），供 Grafana 做事件频率、报警趋势面板——投影永远可从 Postgres/边缘日志重建。

### 5.3 上行协议（Edge → Central）

```text
POST /api/v1/events:batch          Authorization: Bearer <edge-token>
{ "edgeId": "edge-01",
  "events": [ { ...ProductionEvent... }, ... ] }    -- ≤500 条/批

200 { "accepted": 480, "duplicates": 20, "ackSeq": 18234 }
```

- 幂等：`event_id` 主键 `ON CONFLICT DO NOTHING`，重发无害
- 断点：Shipper 按 `seq` 顺序推进，收到 `ackSeq` 后标记 `ship_state=1`；进程重启从最小未确认 seq 继续
- 重试：指数退避（1s → 60s 封顶），断网期间事件在 outbox 累积（NFR7）
- 顺序：单边缘按 seq 保序上行；跨边缘不保证全序，消费端按 `occurred_at` 做业务排序

### 5.4 查询与订阅 API

| 端点 | 位置 | 说明 |
|---|---|---|
| `GET /api/v1/events` | Edge + Central | 过滤：`type`、`subjectType`、`subjectId`、`correlationId`、`from`、`to`、`ctx.<key>=<value>`；游标分页 |
| `GET /api/v1/events/stream` | Edge + Central | SSE 实时流，支持同样过滤；`Last-Event-ID`（=seq）断线续读 |
| `GET /api/v1/cycles/{correlationId}` | Central | 聚合视图：一个周期的 started/completed 及区间内同主体事件 |
| `POST /api/v1/subscriptions` | Central（Phase 3 后期） | Webhook 订阅，CloudEvents 1.0 格式投递，按 event_id 幂等 |

边缘与中心共享同一套查询契约（放入现有 `DataAcquisition.Contracts`），消费者代码可在两级之间平移。MQTT（Sparkplug）/NATS 出口留待 §10 的触发条件出现后再评估。

### 5.5 事件类型版本策略

- 载荷结构变更 → `EventTypeVersion` 递增；只允许加字段，删/改语义字段必须新增事件类型
- 消费者按 `(EventType, EventTypeVersion)` 决定解析方式，未知版本按最近已知版本宽容解析
- Profile 文件自带 `SchemaVersion`，与源配置一致的"先校验后运行"

---

## 6. 可靠性与规模

### 6.1 负载估算

以一个中等规模现场为参照（20 台 PLC/边缘，50 边缘/中心）：

| 数据 | 速率估算 | 结论 |
|---|---|---|
| 遥测/边缘 | 20 PLC × 10 字段 × 10 Hz ≈ 2,000 点/s | 现有 Influx 批量写入链路已覆盖，不动 |
| 事件/边缘 | 周期 30–120s/台 × 2 事件/周期 × 20 台 ≈ 0.3–1.3 ev/s；报警风暴峰值 < 100 ev/s | SQLite WAL 单表插入数千行/s，余量 >10× |
| 事件/中心 | 50 边缘 ≈ 15–65 ev/s 均值 → 数千万行/月 | 按月分区 + 索引可承载（NFR6 的 500 ev/s 仍有 ~10× 冗余）；持续增长时细化分区粒度（§10） |
| 事件日志磁盘/边缘 | ~1KB/事件 × 0.3–1.3 ev/s × 7 天 ≈ 200–800MB | 保留策略内可控 |

数量级结论：**事件平面比遥测平面低 2–3 个数量级**，这是"事件可以享受强保障而不牺牲简单性"的物理基础。

### 6.2 失败语义总表

| 场景 | 行为 | 语义 |
|---|---|---|
| 遥测写 TSDB 失败 | 记日志+指标，丢弃批次（现状） | at-most-once |
| 事件落本地日志失败（磁盘满/损坏） | 记日志+指标+健康检查置降级；事件丢失是**显式故障** | 落盘是唯一硬依赖 |
| 事件 TSDB 投影失败 | 忽略并计数，可随时重建 | 派生数据 |
| 事件上行失败/断网 | outbox 累积，退避重试，恢复续传 | at-least-once |
| 中心收到重复 | event_id 冲突静默去重 | 幂等 |
| 边缘进程崩溃 | 已落盘事件无损；进行中的周期由现有恢复机制产生 diagnostic 事件 | 与现状一致 |
| backlog 触顶 | 丢最旧 + diagnostic 事件 + 指标 | 显式、可观测的降级 |

### 6.3 时钟与排序

- 全链路 UTC（现有原则不变）；`OccurredAt` 用于业务排序，`(EdgeId, Seq)` 用于技术定序
- 不引入混合逻辑时钟：单边缘内 seq 已单调，跨边缘业务上按时间排序足够；出现跨边缘因果需求时再评估（§10）

### 6.4 新增可观测性指标

延续 prometheus-net 体系：`event_emitted_total{type}`、`event_emit_latency_ms`、`event_outbox_backlog`、`event_ship_failures_total`、`event_backlog_dropped_total`、`context_state_entries`。Central 侧：`event_ingest_total{edge}`、`event_ingest_duplicates_total`、`edge_seq_gap_detected_total`。

---

## 7. 与现有代码的映射

原则：**遥测平面零改动；事件平面全部通过新增接口进入，不重写既有类。**（表中路径为更名前布局，命名映射见 §12）

| 层 | 现有 | 动作 |
|---|---|---|
| Domain | `Models/DataMessage.cs`（含 EventType/DiagnosticEventType） | 保留原样服务遥测平面；新增 `Events/ProductionEvent.cs`、`Events/ObjectRef.cs`、`Events/EventRule.cs` 配置模型 |
| Domain | `Models/DeviceConfig.cs`（SchemaVersion=1） | 泛化为源配置：SchemaVersion 2 新增 `SourceCode`（PlcCode 永久别名）、`Adapter`、`Profile`、`EventRules`；v1 反序列化路径不变 |
| Application | `IQueueService` / `IDataStorageService` | 不动。新增 `IEventSink`（发出）、`IEventLog`（outbox 读写）、`IEdgeContextStore`（上下文）、`IEventShipper`（上行） |
| Infrastructure | `DataAcquisitions/ChannelCollector.cs` | 条件采集触发判定抽出为 `EventRuleEvaluator`；`ChannelCollector` 保持遥测职责，Conditional 路径改为调用求值器（行为等价） |
| Infrastructure | `Clients/*`（Hsl 驱动体系） | 原样保留，整体成为 **PLC 适配器**的内部实现；`ISourceAdapter` 接口在第二个适配器（OPC UA）立项时抽取（D10） |
| Infrastructure | `DataAcquisitions/AcquisitionStateManager.cs` | 泛化为 `EdgeStateStore`：保留 `active_cycles` 表与接口，新增按资产键控的 `context_state` 表；`IAcquisitionStateManager` 契约不破坏 |
| Infrastructure | `Queues/QueueService.cs` | 不动。事件 TSDB 投影复用它：投影器把事件转成 `DataMessage` 后 `PublishAsync`，现有面板兼容 |
| Infrastructure | 新增 | `Events/SqliteEventLog.cs`、`Events/EventShipper.cs`、`Events/InfluxEventProjection.cs`、`Profiles/ProfileLoader.cs` |
| Edge.Agent | `--validate-configs` | 扩展：校验 EventRules 与 Profile 引用；新增 `/api/v1/events` 本地查询与 SSE |
| Central.Api | 注册/心跳/代理 | Phase 3 新增 ingest/query/stream 控制器 + Postgres 仓储 |
| Central.Web | Edges/Metrics/Logs 页 | Phase 3 新增 Events 事件流页（按类型/主体/上下文过滤） |
| Contracts | `PlcWriteRequest` | 参数下发链路补发 `parameter.applied` 事件——写 PLC 成功即事实成立 |
| schemas | `device-config.schema.json`（const 1） | 新增 `device-config.v2.schema.json` 与 `profile.schema.json` |
| Simulator | PLC 模拟器 | 增加"剧本模式"：换批次→换模→周期→报警的时间线脚本，作为 Phase 2 验收与 e2e 测试基础 |

新增核心契约（Application 层）：

```csharp
public interface IEventSink
{
    /// <summary>发出事件：同步落盘，异步分发。返回即表示事实已持久。</summary>
    ValueTask EmitAsync(ProductionEvent evt, CancellationToken ct = default);
}

public interface IEventLog
{
    Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default);
    IAsyncEnumerable<ProductionEvent> ReadPendingAsync(int max, CancellationToken ct = default);
    Task MarkShippedAsync(long upToSeq, CancellationToken ct = default);
    IAsyncEnumerable<ProductionEvent> QueryAsync(EventQuery query, CancellationToken ct = default);
}

public interface IEdgeContextStore
{
    string? Get(ObjectRef asset, string key);
    Task SetAsync(ObjectRef asset, string key, string value, CancellationToken ct = default);
    IReadOnlyDictionary<string, string> Snapshot(ObjectRef asset, IReadOnlyList<string> keys);
}
```

---

## 8. 演进路线

每阶段独立可交付、可演示；写明"不做"以抵抗范围蔓延。

**第 0 步是更名**：仓库与命名空间更名为 Ingot，在 Phase 1 动工前一次完成（§12）——此刻代码量最小，成本永远不会更低。

### Phase 1 — 事件底座（预计 2–4 周投入量）

- 交付：`ProductionEvent` + `SqliteEventLog`（outbox）+ `EventRuleEvaluator`（仅 EdgePair，由 v1 Conditional 自动映射）+ 本地 `/api/v1/events` 查询与 SSE + 事件的 Influx 投影 + diagnostic 事件迁移
- 验收：模拟器跑周期 → `events.db` 出现成对 `cycle.started/completed`（共享 CorrelationId）；`kill -9` 重启后已落盘事件无损、恢复诊断以 `diagnostic.*` 事件出现；v1 配置零改动，现有遥测行为与面板不变
- 不做：上下文、新触发器种类、Profile、中心侧、任何新外部依赖

### Phase 2 — 上下文与规则：光学闭环（1–2 个月投入量）

- 交付：`ValueChanged`/`BitFlag`/`Threshold` 触发器 + `SetContext`/`ContextKeys` + 按资产键控的 `EdgeStateStore.context_state` + SchemaVersion 2 配置与校验（源中立命名：`SourceCode`/`Adapter`/`Tag`，资产与源分离）+ `profiles/core` + `profiles/optical` + 模拟器剧本模式
- 验收（就是 §4.4 那条查询）：剧本"换批次→换模→跑 3 个周期→报警并恢复"结束后，用一条本地 API 查询按 `material_lot` 还原完整事件链，周期事件自动携带批次与模具上下文
- 不做：中心事件库、订阅分发、第二个行业 Profile、Profile 高级机制（继承链/属性校验）

### Phase 3 — 中心事件枢纽（1–2 个月投入量）

- 交付：Central ingest（幂等批量）+ Postgres 事件库（分区 + GIN）+ 中心查询/SSE + `EventShipper`（重试/断点/背压）+ Central Web 事件流页 + per-edge token + `docker-compose.events.yml`
- 验收：拔网线 2 小时再恢复，中心事件无缺口无重复（按 edge seq 与 event_id 验证）；两类消费者并行演示——报表脚本（SQL 直查）与 webhook 接收器（CloudEvents 格式）
- 不做：消息中间件、多租户、复杂权限、跨边缘复合事件

### Phase 4 — 生产语言：行业模型库（按需启动）

- **启动门槛**：光学闭环在 ≥2 个真实现场稳定运行后才启动本阶段——这是"先长出来，再抽象"的硬性检查点
- 交付：从光学 Profile 中提炼第二个行业 Profile（验证抽象是否成立）+ Profile 版本化与打包 + 事件类型 registry + MQTT(Sparkplug)/NATS 出口（如触发条件成立）+ 面向 AI 的周期特征视图（cycle 聚合表 → 训练样本窗口）
- 验收：新行业接入只写 Profile 与源配置，核心代码零修改

**适配器轨道**（与行业轨道正交，按真实需求启动，可与 Phase 3/4 并行）：

- 第二个源适配器优先 **OPC UA**——订阅范式 + 源时间戳 + 质量码，与 PLC 轮询互补性最大，一次验证 §3.4 的全部抽象
- 届时才抽取 `ISourceAdapter` 接口（rule of two）；验收 = 事件层、存储、API、中心侧**零改动**接入新源
- 视觉/机器人推送源、DCS/historian 批量回填按 §10 触发条件排队

```text
Phase 1          Phase 2            Phase 3             Phase 4
事件成为一等公民   事件带上下文        多边缘汇聚可订阅       行业语言沉淀
(单机,无新依赖)   (光学闭环,可追溯)    (+Postgres,中心枢纽)  (≥2 现场后启动)
     现有用户零感知 ────────────────► 增量启用 ──────────► 生态扩展
```

---

## 9. 取舍分析

| # | 决策 | 备选 | 选择与理由 | 接受的代价 |
|---|---|---|---|---|
| D1 | 双平面（遥测/事件分离） | 全部数据走 outbox；或全部维持丢弃语义 | 分离。可靠性按数据价值分级，遥测简单性与事件持久性兼得 | 两套语义需要清晰文档；投影引入少量冗余存储 |
| D2 | 边缘 SQLite outbox | 引入 MQTT/Kafka/NATS | SQLite。零新依赖、单二进制哲学、速率余量 10×；分发用 HTTP 拉/推足够 | 扇出能力弱，多消费者场景延后（§10 有触发器） |
| D3 | 中心事件库用 Postgres | 继续只用 InfluxDB | Postgres。事件要按上下文任意过滤、与主数据 join，JSONB+GIN 是正解；TSDB 的 tag 基数与查询模型不适合 | Phase 3 新增一个存储组件（此前两阶段完全不需要） |
| D4 | 边缘派生事件 | 原始数据上云后中心派生 | 边缘。低延迟、断网自治、贴近现有代码结构 | 跨设备复合事件（整线节拍）暂缺，留给中心层未来做 |
| D5 | core taxonomy + Profile | 每行业硬编码；或完全自由字符串 | 中间路线。核心统一保证互操作，Profile 吸收行业差异 | Profile 机制本身是抽象成本——用"两个真实场景"门槛控制 |
| D6 | 对齐 CloudEvents（映射） | 完全自定义信封；或内部直接用 CloudEvents | 映射。内部扁平高效，出口标准化，生态零适配 | 维护一层序列化映射 |
| D7 | at-least-once | exactly-once | ALO + 消费端按 event_id 幂等。EO 需要分布式事务级复杂度，工业现场不可维护 | 消费者必须实现去重（文档与 SDK 示例承担） |
| D8 | UUIDv7 事件 ID | ULID 库；沿用 GUIDv4 | `Guid.CreateVersion7()`，时间有序利于索引，且零新依赖 | 无；既有 CycleId 保持 GUIDv4 不迁移 |
| D9 | 上下文存快照 | 存对象引用，查询时 join | 快照。历史事实不因主数据变更而漂移，追溯可信 | 事件体积略增（上下文通常 <200B，可忽略） |
| D10 | 命名/契约立即源中立，适配器接口延后抽取 | 现在就设计 `ISourceAdapter`；或全部推迟 | 拆开处理。v2 契约未发布，更名此刻零成本；而接口抽象没有第二个实现校准，容易设计错 | v2 落地后短期内只有 PLC 一种源，"中立"的收益暂时不可见 |
| D11 | 立即更名 Ingot | 发布 1.0 后再改；保持原名；或用 ManuCore/Takt/Acta 等候选（检索证实均有同类占用，§12.1） | 立即，且选品类干净的 Ingot。外部引用尚少、GitHub 自动重定向、代码量最小的时刻 | 一次性全仓 PR；历史外链短期失效（重定向缓解） |

---

## 10. 何时重新审视

| 触发条件 | 重新评估 |
|---|---|
| 单中心接入边缘 >100，或出现 ≥3 个实时消费者 | 引入 broker（NATS JetStream / MQTT+Sparkplug）替代 HTTP 上行与 SSE 扇出 |
| 出现跨设备/跨边缘复合事件需求（整线 OEE、瓶颈分析） | 中心侧流处理层（先物化视图，再考虑流引擎） |
| 中心事件量持续 >500 ev/s | Timescale/分区细化、批量摄入管道 |
| 出现第 3 个行业 Profile | Profile registry 服务化、schema 治理、兼容性测试矩阵 |
| 多工厂/多法人隔离需求 | 租户模型、认证体系升级（当前 per-edge token 到期） |
| AI 消费者从"读事件"变为"要特征" | 独立的特征视图/物化层，避免把 ML 语义压进事件模型 |
| 第二类源的真实需求落地（OPC UA / CNC / 视觉 / 机器人） | 抽取 `ISourceAdapter`、订阅式采集、原生事件透传（`SourceEvent` 触发器） |
| 流程工业（DCS / historian）场景出现 | 批量回填与历史补录语义；ISA-88 批次概念是否进 core taxonomy（仍守"两个场景"门槛） |

---

## 11. 参考

- 项目现状：`README.md`、`docs/design.md`、`docs/modules.md`、`schemas/device-config.schema.json`
- [CloudEvents 1.0](https://cloudevents.io/) — 事件信封字段对齐目标
- [United Manufacturing Hub](https://github.com/united-manufacturing-hub/united-manufacturing-hub) 与其 [Unified Namespace 文档](https://umh.docs.umh.app/docs/architecture/data-infrastructure/unified-namespace/) — 重基建路线的同方向参照
- [Rhize（Libre Technologies）](https://www.libremfg.com/) — ISA-95 制造数据枢纽的商业参照
- ISA-95 / IEC 62264（设备与物料层级词汇）、ISA-TR88 PackML（state.changed 词汇参考）——只借词汇，不引入其全部复杂度
- OPC UA / IEC 62541（订阅范式、源时间戳与质量码语义）——适配器轨道第二个适配器的目标协议

---

## 12. 产品命名：Ingot（原 DataAcquisition）

### 12.1 名字的含义

更名是定位声明的一部分：项目名不再描述动作（采集），而描述产物。**Ingot = 锭**——矿石经熔炼后铸成的标准金属块。这个隐喻与产品本质逐点对应：

| 锭的属性 | 产品中的对应 |
|---|---|
| 由矿砂熔炼而来 | 遥测是矿砂：量大、单位价值低；事件平面把它炼成业务事实——正是 §3.2 双平面数据分级的隐喻版本 |
| 铸成即定型，不可回炉篡改 | 事件是不可变的业务事实（§4.1），落盘即"已发生" |
| 形制标准，可堆叠、可储存 | 统一事件信封与 core taxonomy（§4.1–4.2），append-only 事件日志（§5.1） |
| 是价值储存与流通的单位 | 事件是 MES / 报表 / 追溯 / AI 消费的标准价值单元，最终沉淀为"生产语言"的词汇（§1.2 梯子的顶端） |
| 铸造与钢厂的原生词汇 | 对制造业用户零解释成本；中文语境另有"金锭 / 元宝"的价值联想 |

一句话版本，供 README 与对外介绍使用：

> **Ingot — 把生产数据炼成事实。** Smelt production data into facts.

命名的工程属性：5 个字母、两音节、完全表音，听一遍即可拼对；`Ingot.Edge.Agent` 等命名族自然；检索区分度好。定名过程中曾评估并否决 ManuCore、Manufact、Takt、Acta、Faktwerk、Ergograph、Datum、Facto 等候选，均因同类/邻类占用或读写成本落选；Ingot 经检索（2026-07）未见同名软件产品或开源项目，已知远类目占用仅一家金融经纪商，风险可接受。

### 12.2 更名映射与执行

| 项 | 现名 | 新名 |
|---|---|---|
| 仓库 | liuweichaox/DataAcquisition | liuweichaox/Ingot（GitHub 自动重定向旧链接与 git remote） |
| 解决方案 | DataAcquisition.sln | Ingot.sln |
| 类库项目/命名空间 | DataAcquisition.Domain / .Application / .Infrastructure / .Contracts | Ingot.Domain / .Application / .Infrastructure / .Contracts |
| 运行时项目 | DataAcquisition.Edge.Agent / .Central.Api / .Central.Web | Ingot.Edge.Agent / .Central.Api / .Central.Web |
| 工具项目 | DataAcquisition.Simulator / tests/DataAcquisition.Core.Tests | Ingot.Simulator / tests/Ingot.Core.Tests |
| Schema `$id` | …/DataAcquisition/schemas/… | …/Ingot/schemas/…（随 v2 schema 一起换） |
| 未来 NuGet 包 | —（尚未发布） | Ingot.*（首发即新名，无历史包袱） |

执行顺序（一次 PR 完成，Phase 1 动工前）：

1. 例行确认命名资产：GitHub org、NuGet 前缀 `Ingot.*`、ingot.io / ingot.dev 域名、目标市场商标库
2. GitHub 仓库更名（平台自动重定向）
3. 全仓替换：`.sln`、`.csproj`、命名空间与 `using`、docker-compose 服务名、CI 引用
4. README / docs / 徽章 / 示例路径同步
5. 置顶公告 issue，附本 RFC 说明更名与新定位

**刻意不改的**：`Driver` 名称目录、v1 配置的全部字段名（`PlcCode` 等永久兼容）、`Data/` 下数据库文件名——用户现场的存量资产一律不动。
