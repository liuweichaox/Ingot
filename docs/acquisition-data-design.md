# 现场采集数据参考设计

> 文档状态：**当前模型说明与设计参考**。本文说明 Ingot 的通用采样表，并为现场适配器提供可按需采用的 `site_` 本地表设计。实际接入契约见[数据接入](data-connection.md)，Platform 正式关系表见[数据模型总览](data-model.md)。

## 目标与边界

现场设备、已有采集程序或业务系统可使用自己的数据库，也可直接生成事件。适配器负责读取和转换数据、识别运行、保留来源并向 Edge 发送标准事件；Edge 使用 SQLite 持久化并上报事件，Platform 使用 PostgreSQL 保存正式业务数据。

设计记录来源身份、读取时间、值与单位转换、实际运行归属和检验关联。现场按需要选择参考表；计划设定与实际回读分别记录，运行与检验通过可核实的身份关联。

## 数据流与职责

```text
设备 / 已有采集程序 / 检验来源
  → 现场适配器：来源记录、单位转换、运行识别、上报状态
  → Edge：认证、事件日志、上报与故障隔离
  → Platform：正式运行事实、过程样本与质量检验
  → 准入检查 → 分析和建议
```

后文的 `site_` 结构展示现场适配器可维护的本地信息。Edge 使用 SQLite 事件日志持久化并上报事件，Platform 使用 PostgreSQL 正式采样表保存过程数据投影。适配器分别记录 Edge 接收、Platform 接收、检验复核和优化准入状态。设备控制、PLC 心跳与写入由现场系统负责。

## Ingot 已有的采样表

以下三张是 Ingot Platform 中**已实现的表**，描述 Platform 接收的正式样本事实；后文以 `site_` 开头的表用于设计现场适配器本地存储。三张表共同表达“哪个现场的哪台设备，在什么时间、哪次运行中，哪些传感器给出了什么值”：

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

适配器可将同一次读取的 10 个有效值打包成一帧。每秒上报 10 帧、持续 10 秒且全部通过准入时，约形成 100 帧和 1,000 行采样值；实际数量按真实读取与接收记录计算。每帧保留其实际采样时间和有效值。

5 个配方**设定值**保存在 `process_specification_versions` 对应版本中，参数定义和单位来自关联工艺数据模型。采样帧通过 `execution_id` 关联运行事实；配方身份从运行上下文或配方应用事件读取。设备的**实际参数回读**单独记录来源和值。真实运行记录与质量检验建立可靠关联后进入进一步分析。

例如 `process_specification_versions` 中 `process_specification_id=R001`、`version=1` 的记录，`payload` 中的五个设定值如下。参数单位与边界由关联工艺数据模型定义。

```json
{"values":[
  {"code":"temperature_set","value":180},
  {"code":"pressure_set","value":12},
  {"code":"hold_time","value":30},
  {"code":"speed_set","value":20},
  {"code":"position_set","value":50}
]}
```

正式表职责见[数据模型总览](data-model.md)，实际列由[数据库迁移](../src/platform/Ingot.Platform.Infrastructure/Migrations/sql)定义。现场可按站点、设备和信号组织树形点位目录。

## 类型与键的约定

字段类型采用跨数据库逻辑类型：`id` 是稳定字符串，`instant` 是带时区的时间点，`number` 是适合量程和精度的数值，`json` 是结构化数据；部署者按数据库选择实际类型。示例中的“必填”是本地参考设计的建议，具体字段按现场来源与事件契约配置。时间统一存储 UTC，并保留来源时区或偏移；来源时间未知时保留未知状态，同时单独记录接收时间。

点位、设备和运行在现场内使用稳定身份；涉及多工厂时，查询和唯一约束包含 `site_id`。一个运行可以关联多个样本、参数回读及检验记录。来源缺少可靠事件序号时，在本地事务中为每条已接收事实分配并保存稳定标识；通过来源序号和事实身份区分同设备、同时间戳的多次观察。

## 参考表

### 点位定义 `site_point_definition`

现场已有点位配置时，可复用并核对单位、工艺编码、运行关联和来源。例如 DataAcquisition JSON 已包含通道、寄存器、数据类型、字节偏移和换算表达式；适配器据此验证事件契约，并映射为 Platform 点位和事件。

一行代表一个现场点位映射版本，记录来源地址、单位和转换规则；适配器使用这些信息将现场值映射到稳定工艺编码。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `point_id`, `version` | id, id, integer | 必填，复合唯一 | 点位在工厂内的稳定身份与版本。 |
| `equipment_id`, `source_ref` | id, string | 必填 | 设备身份及现场来源引用；来源可为地址、标签或已有系统字段。 |
| `metric_code`, `value_type` | id, string | 必填 | 对应已发布工艺数据模型的编码和数据类型。 |
| `source_unit`, `target_unit` | string | 视点位填写 | 原始与标准单位；无量纲或未知应明确区分。 |
| `conversion_ref`, `effective_from`, `effective_to` | string, instant, instant | 可选 | 版本化换算规则及适用时间；结束时间未知可为空。 |

建议按 `(site_id, equipment_id, metric_code, effective_from)` 查询，并防止同一映射在同一适用范围内出现无法区分的重叠版本。地址和转换规则变更应建立新版本，旧记录仍引用实际使用的版本。

以下示例是同一台设备的 10 个点位；每行还需保存 `site_id=S01`、`equipment_id=PRESS-01`、`version=1`、`value_type` 和 `source_ref`，表中省略这些共同字段。适配器将现场 `point_id` 映射到 Platform 的 `point_key`。

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

一行代表一次可识别的实际运行。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `execution_id` | id, id | 必填，复合唯一 | 工厂和稳定运行身份；输出到事件的 `ExecutionId`。 |
| `equipment_id`, `source_run_ref` | id, string | 必填、可选 | 实际设备和来源侧运行编号；核实来源编号后映射到稳定的全局运行身份。 |
| `started_at`, `completed_at` | instant | 可选 | 来源确认的开始与结束时间；未知时间为空。 |
| `boundary_status`, `context` | string, json | 必填、可选 | 边界完整性与产品、配方版本、批次等可追溯上下文。 |

建议按 `(site_id, equipment_id, started_at)` 检索，并保证运行身份可跨重启恢复。开始事件缺失、信号重叠或身份冲突时，根据现有证据记录边界状态并保留关联材料。

例如一条运行如下；`context` 中的配方标识来自现场可核实的来源。此处展示部分字段。

```text
site_id  execution_id  equipment_id  started_at                 completed_at               boundary_status  context
S01      RUN-001       PRESS-01      2026-01-01T02:00:00.000Z   2026-01-01T02:00:10.000Z  complete         {"process_specification_id":"R001","process_specification_version":"1"}
```

### 原始采样 `site_sample`

一行代表一次来源观察中的一个点位值；高频现场可使用批次或列式存储，并保留来源、时间、点位、质量和版本。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `sample_id` | id, id | 必填，复合唯一 | 稳定的本地样本身份。 |
| `equipment_id`, `point_id`, `point_version` | id, id, integer | 必填 | 实际设备与使用的点位映射版本。 |
| `execution_id` | id | 可选 | 来源身份可靠时填写对应运行；身份待确认时保持为空。 |
| `occurred_at`, `received_at` | instant | 前者未知可为空，后者必填 | 设备发生时间与适配器接收时间；分别保留时间来源。 |
| `source_sequence`, `raw_value`, `source_unit` | string, json, string | 可选 | 来源序号、原始值与单位；按实际类型保留来源值。 |
| `converted_value`, `target_unit`, `quality` | json, string, string | 视来源填写 | 换算值、标准单位及可追溯质量状态；未知质量使用不确定状态。 |

建议按 `(site_id, equipment_id, occurred_at)` 和 `(site_id, execution_id, occurred_at)` 查询，并按实际吞吐设计分区与保留期。`sample_id` 区分同一时刻的多次观察；来源序号用于去重时，按其作用域、回绕及设备重启规则处理。原始值与换算值保持可追溯，修正形成新版本或明确更正记录。

以下示例展示两个采样帧中的前两个点位；同帧的其余 8 个点位按相同结构记录。每行另有 `site_id=S01`、`equipment_id=PRESS-01`、`point_version=1`、`received_at` 和质量等字段。

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
| `observed_at`, `source_ref`, `quality` | instant, string, string | 按来源填写 | 回读时间、来源与质量；来源未提供或尚待核实时记录为缺失。 |
| `recipe_ref`, `recipe_version` | id, integer | 可选 | 可追溯的配方引用；配方设定与实际回读分别保存。 |

建议按 `(site_id, execution_id, parameter_code, observed_at)` 检索；同一参数的多次回读按时间分别保存，展示当前值时应用明确的业务规则。适配器遵循现场已发布的工艺变量定义。

例如设备在该次运行回读了 5 个参数；以下 `site_id` 均为 `S01`，`execution_id` 均为 `RUN-001`，来源、回读时间、质量及配方引用字段省略。示例数值用于说明结构，正式记录来自实际回读。

```text
readback_id  parameter_code   actual_value  unit
RB-001       temperature_set  179.8         ℃
RB-002       pressure_set     11.9          MPa
RB-003       hold_time        30            s
RB-004       speed_set        20            mm/s
RB-005       position_set     50            mm
```

### 检验关联 `site_quality_link`

一行表示来源检验或样本与运行之间的一条可复核关联；Platform 的检验记录、复核和质量方案由 Ingot 检验流程管理。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `site_id`, `link_id`, `execution_id` | id | 必填，前两项复合唯一 | 关联身份及经核实的运行。 |
| `quality_source`, `quality_record_ref` | string | 必填 | 检验系统及原始检验记录或样本引用。 |
| `method`, `linked_at`, `review_status` | string, instant, string | 必填 | 关联依据、时间和复核状态；歧义应人工处理。 |

建议按 `(site_id, execution_id)` 检索，并按来源身份约束重复关联。检验结果在 Ingot 检验流程登记和复核；现场 `review_status` 记录来源侧状态，Platform 的复核状态来自 Ingot 记录。

例如样本条码经人工核对后形成一条适配器侧关联，并在 Ingot 完成检验登记及复核：

```text
site_id  link_id  execution_id  quality_source  quality_record_ref  method    linked_at                 review_status
S01      QL-001   RUN-001       lab-system      INSPECT-001         barcode   2026-01-01T02:05:00Z      reviewed
```

### 适配器上报记录 `site_event_outbox`

一行代表一份准备提交给 Edge 的不可变事件；现场已有可靠上报组件时可沿用该组件的持久化结构。

| 字段 | 逻辑类型 | 建议 | 含义 |
| --- | --- | --- | --- |
| `event_id` | UUIDv7 | 必填，唯一 | 标准事件身份；首次发送前持久化，重试沿用。 |
| `site_id`, `execution_id` | id | 必填、可选 | 现场归属及可确认的运行身份。 |
| `event_type`, `payload`, `created_at` | string, json, instant | 必填 | 类型、冻结的事件内容与创建时间。 |
| `delivery_state`, `attempt_count`, `last_error` | string, integer, string | 必填、必填、可选 | 本地上报状态、尝试次数和脱敏失败信息。 |
| `edge_accepted_at`, `edge_seq` | instant, integer | 可选 | Edge 接受时间与返回序号；Platform 接收状态单独记录。 |

建议按 `(delivery_state, created_at)` 扫描待发送事件。业务事实和 outbox 记录在同一可靠事务中提交；发送失败时保留原 ID 和内容。Edge 对本地日志保留期内的相同事件重试返回原序号，内容冲突返回 409。超出保留期的重发和队列满时的丢弃进入对账流程；端到端采用至少一次传输与业务幂等。详见[数据接入](data-connection.md#上报与故障处理)。

例如第一帧准备上报时，适配器可保存以下 outbox 记录。完整请求按事件契约补齐其余信号和必填字段；待发送状态下的 `edge_accepted_at` 和 `edge_seq` 为空。

```text
event_id                              site_id  execution_id  event_type      created_at                   delivery_state  attempt_count
019b76da-a800-7000-8000-000000000001  S01      RUN-001       process.sample  2026-01-01T02:00:00.000Z    pending         0
payload 摘录: {"data":{"values":{"temperature":178.5,"pressure":10.8}}}
```

## 与 Ingot 事件的对应

| 现场参考信息 | Ingot 接入含义 | 接入要求 |
| --- | --- | --- |
| 运行身份、设备和时间 | `ExecutionId`、`Subject` 和 `OccurredAt` | 同一次真实执行使用一致身份，并保留实际发生时间。 |
| 点位编码与换算值 | `process.sample` 的 `data.values` | 键和类型符合可解析的已发布工艺数据模型，必填项完整。 |
| 事件身份与来源 | `EventId`、`EventType` 和 `Source` | `EventId` 使用 UUIDv7；Edge 规范来源并设置记录时间、序号及摘要。 |
| 实际参数、过程上下文 | 运行证据及事件上下文 | 保留可核实的来源；配方设定与实际回读分别记录。 |
| 外部检验引用 | 与 Ingot 检验记录可靠关联 | 按质量方案登记检验结果、完成复核并满足分析准入。 |

事件必填字段、批量接口及来源约束见[数据接入](data-connection.md)和代码契约；可用[外部适配器示例](../examples/external-adapter/README.md)验证基础事件接收。

JSON 请求包含契约要求的 `RecordedAt`；Edge 接收后设置平台记录时间。运行开始与结束分别使用 `process.execution.started` 和 `process.execution.completed`，外部适配器根据实际信号形成这些事件。过程样本上下文可包含 `data_model_id` / `data_model_version` 或 `process_specification_id` / `process_specification_version`；Platform 按适用的已发布模型和分析方案执行准入，适配器分别核验数据值与质量。

采样质量按当前事件契约提供。`data.quality.qualityVersion=1` 且 `valueFlags` 为对象时，Platform 读取版本化质量；缺少或无法识别版本时将质量标记为不确定。有效版本化质量中，未单独标记的点位按契约默认值处理。为未知质量的点位显式提供不确定质量码或标记，并核对 Platform 投影结果。空值和来源证据共同决定分析准入。适配器将现场 `quality` 映射到 Platform 的质量事实。

## 实施与验收

1. 确认工厂、设备、运行、点位和检验的身份来源，记录权限边界、字段字典和单位。
2. 对照现场协议资料或已有采集程序，确认采样、触发、时间与质量语义，并据此调整参考表。
3. 对照已发布工艺模型定义编码、类型和单位，保留映射及换算版本，区分设定、实际回读和结果。
4. 在测试环境验证有效值、缺失值、重复、冲突、设备重启、断网重试、队列满和时间偏差；检查 Edge 接受及 Platform 接收分别发生。
5. 使用经授权的真实运行和检验确认关联与复核，验收数据准入、分析可追溯性及现场安全；记录适配器版本和验证证据。

现场依据实际需求制定并演练保留期、容量、访问控制、备份恢复和数据更正策略；将兼容性、安全性与工艺结果纳入现场验收证据。
