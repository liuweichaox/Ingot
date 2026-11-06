# 现场采集数据参考设计

> 文档状态：**当前模型说明与可选设计参考**。本文先解释 Ingot 已有的采样表，再提供现场适配器可选的 `site_` 表设计。参考表不是 Ingot 已创建的表、强制部署要求或设备兼容性承诺。实际接入契约见[数据接入](data-connection.md)，正式关系表见[数据模型总览](data-model.md)。

## 目标与边界

现场设备、已有采集程序或业务系统可以使用自己的数据库，也可以直接生成事件。适配器负责读取和转换数据、识别运行、保留来源并向 Edge 发送标准事件；Edge 负责事件接收、本地持久化和向 Platform 上报。适配器的数据库不等于 Edge 的事件日志或 Platform 的正式业务库。

设计应能回答：谁在何时从哪里读取了什么，值和单位如何转换，它是否属于某次真实运行，以及后续检验如何可靠关联。现场不需要所有参考表，但不得用计划设定冒充实际回读，也不能仅凭时间邻近猜测运行与检验的对应关系。

## 数据流与职责

```text
设备 / 已有采集程序 / 检验来源
  → 现场适配器：来源记录、单位转换、运行识别、上报状态
  → Edge：认证、事件日志、上报与故障隔离
  → Platform：正式运行事实、过程样本与质量检验
  → 准入检查 → 分析和建议
```

后文的 `site_` 结构只描述适配器可能维护的本地信息，Ingot 不创建这些表。Edge 的 SQLite 本地事件日志和 Platform 的 PostgreSQL 正式采样表是两个已经实现的独立存储，分别用于事件持久化与上报、正式过程数据投影。事件被 Edge 接受不表示 Platform 已接收，检验关联不表示其已通过复核或优化准入。控制设备、PLC 心跳和写入属于现场系统责任，不由这些表授予 Ingot 控制能力。

## Ingot 已有的采样表

以下三张是 Ingot Platform 中**已实现的表**，不同于后文以 `site_` 开头的现场参考表。它们共同表达“哪个现场的哪台设备，在什么时间、哪次运行中，哪些传感器给出了什么值”：

- `collection_points`：点位目录。每个点位记录站点、Edge、对象和信号代码，通过 `point_key` 标识；不同机器可以拥有不同的点位集合。
- `process_sample_frames`：采样帧。保存 `frame_id`、`occurred_at`、`site_id`、`edge_id`、对象、可选 `execution_id` 等。一个通过平台准入的 `process.sample` 事件形成一帧。
- `process_sample_values`：采样值。每行保存帧的 `frame_id`、`occurred_at`、点位的 `point_key`、质量信息和一个类型化的值。按帧关联时同时使用 `frame_id` 和 `occurred_at`，再通过 `point_key` 关联点位目录。

例如，一次模压 `RUN-001` 使用配方 `R001/v1`，设备 `PRESS-01` 有 10 个传感器。下面仅显示 2 个传感器和 2 帧；时间和值均为示意。

```text
collection_points（点位目录）
point_key  site_id  subject_id  signal_code
101        S01      PRESS-01    temperature
102        S01      PRESS-01    pressure
…          …        …           …

process_sample_frames（采样帧）
frame_id  occurred_at              site_id  subject_id  execution_id
1001      2026-01-01T02:00:00.000Z S01      PRESS-01    RUN-001
1002      2026-01-01T02:00:00.100Z S01      PRESS-01    RUN-001

process_sample_values（采样值；数值型示例）
frame_id  occurred_at              point_key  numeric_value
1001      2026-01-01T02:00:00.000Z 101        178.5
1001      2026-01-01T02:00:00.000Z 102        10.8
1002      2026-01-01T02:00:00.100Z 101        178.6
1002      2026-01-01T02:00:00.100Z 102        10.9
```

如果适配器把同一次读取的 10 个有效值打包成一帧、每秒上报 10 帧，运行 10 秒且全部通过准入，就得到约 100 帧和 1,000 行采样值。实际数量取决于真实读取和接收情况；缺失值不会被当作有效值补造，不同采样时间也不能伪装成同时发生。

5 个配方**设定值**不重复存入每个采样值：配方版本和设定保存在 `process_specification_versions` 的版本记录中，参数定义和单位来自关联工艺数据模型。采样帧通过 `execution_id` 与运行事实关联，配方身份需要结合运行的上下文或配方应用事件核对；`process_sample_frames` 没有直接保存配方编号的专用列。设备的**实际参数回读**也应有来源，不能用设定值冒充。真实记录和质量检验须可靠关联后才能用于进一步分析。

例如 `process_specification_versions` 中 `process_specification_id=R001`、`version=1` 的一行，其 `payload` 中五个设定值可表示为下面的摘录。`payload` 还包含其他配方字段；参数单位与边界由关联工艺数据模型定义。

```json
{"values":[
  {"code":"temperature_set","value":180},
  {"code":"pressure_set","value":12},
  {"code":"hold_time","value":30},
  {"code":"speed_set","value":20},
  {"code":"position_set","value":50}
]}
```

当前表职责见[数据模型总览](data-model.md)；实际列及其演进以[数据库迁移](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql)为准。机器与测点可以按现场、设备和信号组织成树形浏览，但这并不意味着当前数据库是 IoTDB，也不改变实际接收契约。

## 类型与键的约定

字段类型采用跨数据库逻辑类型：`id` 是稳定字符串，`instant` 是带时区的时间点，`number` 是适合量程和精度的数值，`json` 是结构化数据；部署者按数据库选择实际类型。示例中的“必填”是此参考设计的建议，不表示 Platform 或所有现场数据源强制要求相同字段。时间建议统一存储 UTC 并保留来源时区或偏移；来源时间未知时保留未知状态，不用接收时间伪装。

点位、设备和运行在现场内必须有稳定身份；涉及多工厂时，查询和唯一约束应包含 `site_id`。一个运行可有多个样本、参数回读及检验记录。若来源没有可靠事件序号，应在本地事务中为每条已接收事实分配并保存稳定标识；不能假定相同设备和时间戳就是重复数据。

## 参考表

### 点位定义 `site_point_definition`

如果现场已有点位配置，应优先复用并核对实际语义，无需为了接入 Ingot 新建此表。例如 DataAcquisition 的 JSON 已包含通道、寄存器、数据类型、字节偏移和换算表达式；适配器仍须核实单位、稳定工艺编码、运行关联和来源，不能假定这些字段已经满足 Ingot 事件契约。配置文件本身不会自动转化为平台点位或事件。

一行代表一个现场点位映射版本；用于解释来源地址、单位和转换，不要求 Ingot 保存或使用现场协议地址。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `point_id`, `version` | id, id, integer | 必填，复合唯一 | 点位在工厂内的稳定身份与版本。 |
| `equipment_id`, `source_ref` | id, string | 必填 | 设备身份及现场来源引用；来源可为地址、标签或已有系统字段。 |
| `metric_code`, `value_type` | id, string | 必填 | 对应已发布工艺数据模型的编码和数据类型。 |
| `source_unit`, `target_unit` | string | 视点位填写 | 原始与标准单位；无量纲或未知应明确区分。 |
| `conversion_ref`, `effective_from`, `effective_to` | string, instant, instant | 可选 | 版本化换算规则及适用时间；结束时间未知可为空。 |

建议按 `(site_id, equipment_id, metric_code, effective_from)` 查询，并防止同一映射在同一适用范围内出现无法区分的重叠版本。地址和转换规则变更应建立新版本，旧记录仍引用实际使用的版本。

以下示例是同一台设备的 10 个点位；每行还需保存 `site_id=S01`、`equipment_id=PRESS-01`、`version=1`、`value_type` 和 `source_ref`，表中省略这些共同字段。`point_id` 是现场参考表的标识，与前述平台 `point_key` 属于不同命名空间，应通过适配映射对应。

```text
point_id  metric_code   target_unit
P01       temperature   ℃
P02       pressure      MPa
P03       position      mm
P04       speed         mm/s
P05       vacuum        kPa
P06       current_1     A
P07       current_2     A
P08       voltage_1     V
P09       voltage_2     V
P10       temperature_2 ℃
```

### 运行 `site_run`

一行代表一次可识别的实际运行，而非一条配方建议或计划工单。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `execution_id` | id, id | 必填，复合唯一 | 工厂和稳定运行身份；输出到事件的 `ExecutionId`。 |
| `equipment_id`, `source_run_ref` | id, string | 必填、可选 | 实际设备和来源侧运行编号。来源编号不应替代未核实的全局运行身份。 |
| `started_at`, `completed_at` | instant | 可选 | 来源确认的开始与结束时间；未发生或未知为空。 |
| `boundary_status`, `context` | string, json | 必填、可选 | 边界完整性与产品、配方版本、批次等可追溯上下文。 |

建议按 `(site_id, equipment_id, started_at)` 检索；运行跨重启的身份必须可恢复。开始事件缺失、信号重叠或身份冲突时标记不完整并保留证据，不应补造完整边界。

例如一条运行如下；`context` 中的配方标识需有现场来源，不能凭时间推测。此处仅展示部分字段。

```text
site_id  execution_id  equipment_id  started_at                 completed_at               boundary_status  context
S01      RUN-001       PRESS-01      2026-01-01T02:00:00.000Z   2026-01-01T02:00:10.000Z  complete         {"process_specification_id":"R001","process_specification_version":"1"}
```

### 原始采样 `site_sample`

一行代表一次来源观察中的一个点位值；高频现场也可使用批次或列式存储，但须能恢复相同来源、时间、点位、质量和版本。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `sample_id` | id, id | 必填，复合唯一 | 稳定的本地样本身份。 |
| `equipment_id`, `point_id`, `point_version` | id, id, integer | 必填 | 实际设备与使用的点位映射版本。 |
| `execution_id` | id | 可选 | 可靠识别后关联的运行；未知为空，不按邻近时间猜测。 |
| `occurred_at`, `received_at` | instant | 前者未知可为空，后者必填 | 设备发生时间与适配器接收时间；分别保留时间来源。 |
| `source_sequence`, `raw_value`, `source_unit` | string, json, string | 可选 | 来源序号、原始值与单位；不支持的类型不得静默转为数值。 |
| `converted_value`, `target_unit`, `quality` | json, string, string | 视来源填写 | 换算值、标准单位及可追溯质量状态；未知质量不是良好。 |

建议按 `(site_id, equipment_id, occurred_at)` 和 `(site_id, execution_id, occurred_at)` 查询，并按实际吞吐设计分区与保留期。`sample_id` 用于区分同一时刻的不同观察；若来源序号用于去重，先确认其作用域、回绕及设备重启行为。原始值与换算值应可追溯，修正形成新版本或明确更正记录。

例如前两帧中的两个点位如下；每行另有 `site_id=S01`、`equipment_id=PRESS-01`、`point_version=1`、`received_at` 和质量等字段。这里只展示 4 个值，另外 8 个点位及其他时刻省略，不表示实际只有两个传感器。

```text
sample_id  execution_id  occurred_at               point_id  raw_value  converted_value  target_unit
SAMP-001   RUN-001       2026-01-01T02:00:00.000Z  P01       178.5      178.5            ℃
SAMP-002   RUN-001       2026-01-01T02:00:00.000Z  P02       10.8       10.8             MPa
SAMP-003   RUN-001       2026-01-01T02:00:00.100Z  P01       178.6      178.6            ℃
SAMP-004   RUN-001       2026-01-01T02:00:00.100Z  P02       10.9       10.9             MPa
```

### 实际参数 `site_parameter_readback`

一行代表某次运行中一个参数的实际回读或明确的来源结果。可按需要与采样表合并，但语义必须可区分。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `readback_id`, `execution_id` | id | 必填，前两项复合唯一 | 记录身份和所属真实运行。 |
| `parameter_code`, `actual_value`, `unit` | id, json, string | 必填，单位按参数定义 | 工艺变量编码、实际值和单位。 |
| `observed_at`, `source_ref`, `quality` | instant, string, string | 按来源填写 | 回读时间、来源与质量；无法证实回读时应显示缺失。 |
| `recipe_ref`, `recipe_version` | id, integer | 可选 | 可追溯的配方引用；配方设定与实际回读分别保存。 |

建议按 `(site_id, execution_id, parameter_code, observed_at)` 检索；同一参数可多次回读，不应在未定义业务规则时覆盖。适配器应遵循现场已发布的工艺变量定义。

例如设备在该次运行回读了 5 个参数；以下 `site_id` 均为 `S01`，`execution_id` 均为 `RUN-001`，来源、回读时间、质量及配方引用字段省略。具体数值仅为示意，只有真实回读才能填写。

```text
readback_id  parameter_code   actual_value  unit
RB-001       temperature_set  179.8         ℃
RB-002       pressure_set     11.9          MPa
RB-003       hold_time        30            s
RB-004       speed_set        20            mm/s
RB-005       position_set     50            mm
```

### 检验关联 `site_quality_link`

一行表示来源检验或样本与运行之间的一条可复核关联；它不是 Platform 的检验记录，也不替代检验、复核或质量方案。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `link_id`, `execution_id` | id | 必填，前两项复合唯一 | 关联身份及经核实的运行。 |
| `quality_source`, `quality_record_ref` | string | 必填 | 检验系统及原始检验记录或样本引用。 |
| `method`, `linked_at`, `review_status` | string, instant, string | 必填 | 关联依据、时间和复核状态；歧义应人工处理。 |

建议按 `(site_id, execution_id)` 检索，并按来源身份约束重复关联；检验结果本身应通过 Ingot 的检验流程登记和复核。不要把现场表的 `review_status` 当作 Ingot 已完成复核的证明。

例如样本条码经人工核对后形成一条关联；这是适配器侧的记录，仍需在 Ingot 完成检验登记及复核：

```text
site_id  link_id  execution_id  quality_source  quality_record_ref  method    linked_at                 review_status
S01      QL-001   RUN-001       lab-system      INSPECT-001         barcode   2026-01-01T02:05:00Z      reviewed
```

### 适配器上报记录 `site_event_outbox`

一行代表一份准备提交给 Edge 的不可变事件；若现场已有可靠上报组件，可以不建此表。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `event_id` | UUIDv7 | 必填，唯一 | 标准事件身份；首次发送前持久化，重试沿用。 |
| `site_id`, `execution_id` | id | 必填、可选 | 现场归属及可确认的运行身份。 |
| `event_type`, `payload`, `created_at` | string, json, instant | 必填 | 类型、冻结的事件内容与创建时间。 |
| `delivery_state`, `attempt_count`, `last_error` | string, integer, string | 必填、必填、可选 | 本地上报状态、尝试次数和失败信息；不得包含凭据。 |
| `edge_accepted_at`, `edge_seq` | instant, integer | 可选 | Edge 接受时间与返回序号；不代表 Platform 已接收。 |

建议按 `(delivery_state, created_at)` 扫描待发送事件。业务事实和 outbox 记录应在同一可靠事务中提交；发送失败保留原 ID 和内容。Edge 对仍在本地日志的相同事件重试返回原序号，内容冲突返回 409；超过本地保留边界后的重发和队列满时的丢弃均须对账处理，不能假设端到端永久幂等。详见[数据接入](data-connection.md#上报与故障处理)。

例如第一帧准备上报时，适配器可保存以下一条 outbox 记录。`payload` 仅展示内容摘录，省略其他信号和事件必填字段，**不能直接作为完整请求发送**；待发送状态下的 `edge_accepted_at` 和 `edge_seq` 为空。

```text
event_id                              site_id  execution_id  event_type      created_at                   delivery_state  attempt_count
019b76da-a800-7000-8000-000000000001  S01      RUN-001       process.sample  2026-01-01T02:00:00.000Z    pending         0
payload 摘录: {"data":{"values":{"temperature":178.5,"pressure":10.8}}}
```

## 与 Ingot 事件的对应

| 现场参考信息 | Ingot 接入含义 | 边界 |
| --- | --- | --- |
| 运行身份、设备和时间 | `ExecutionId`、`Subject` 和 `OccurredAt` | 同一次真实执行使用一致身份；发生时间不能替换为补传时间。 |
| 点位编码与换算值 | `process.sample` 的 `data.values` | 键和类型必须符合可解析的已发布工艺数据模型；必填项须完整。 |
| 事件身份与来源 | `EventId`、`EventType` 和 `Source` | `EventId` 使用 UUIDv7；Edge 规范来源并设置记录时间、序号及摘要。 |
| 实际参数、过程上下文 | 运行证据及事件上下文 | 来源需明确且能核实；设定值不能替代实际回读。 |
| 外部检验引用 | 与 Ingot 检验记录可靠关联 | 仍需满足质量方案、复核和分析准入，不能只提交引用就认定有效。 |

本表不是完整 JSON 示例。事件必填字段、批量接口及来源约束以[数据接入](data-connection.md)和代码契约为准；可用[外部适配器示例](../examples/external-adapter/README.md)验证基础事件接收。

提交 JSON 时仍须提供契约要求的 `RecordedAt`，即使 Edge 接收后会重写它。运行开始与结束分别使用 `process.execution.started` 和 `process.execution.completed`；外部适配器负责根据实际信号形成这些事件。过程样本的上下文可包含 `data_model_id` / `data_model_version` 或 `process_specification_id` / `process_specification_version`，但只有这些标识并不保证准入：Platform 仍须解析到适用的已发布模型和分析方案。数据值非空性和质量需要单独核验，不能把字段存在视为测量有效。

采样质量须按当前事件契约提供；`data.quality.qualityVersion=1` 且 `valueFlags` 为对象时，Platform 才能识别版本化质量信息。缺少或无法识别的版本化质量按不确定处理，但在有效的版本化质量信息中，未单独标记的点位可能被判为良好。不得为了未知质量伪造空 `valueFlags`：应为未知点位显式提供不确定质量码或标记，并核对 Platform 实际投影结果。空值和来源证据仍会影响结果。现场表中的 `quality` 是参考字段，不会自动转换成平台质量事实。

## 实施与验收

1. 确认工厂、设备、运行、点位和检验的身份来源，记录权限边界、字段字典和单位。
2. 用现场协议资料或已有采集程序确定采样、触发、时间与质量的实际语义；按需调整参考表，不依赖未验证的设备假设。
3. 对照已发布工艺模型定义编码、类型和单位，保留映射及换算版本，区分设定、实际回读和结果。
4. 在测试环境验证有效值、缺失值、重复、冲突、设备重启、断网重试、队列满和时间偏差；检查 Edge 接受及 Platform 接收分别发生。
5. 使用经授权的真实运行和检验确认关联与复核，再检查数据准入、分析可追溯性及现场安全；记录适配器版本、证据和限制。

保留期、存储容量、访问控制、备份恢复及数据更正策略由现场依据实际需求制定和演练；此参考设计本身不证明兼容性、安全性或工艺收益。
