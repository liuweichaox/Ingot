# ADR-0006：工艺阶段归属（每条数据属于哪个阶段）

**状态（Status）：** Proposed
**日期（Date）：** 2026-07-20
**决策者（Deciders）：** Ingot 维护者（@liuweichaox）
**依赖：** [ADR-0005](adr-0005-feature-registry.md) 的 phase 维度
**决策：** 边缘上报**配方步序**（事实），中心做**阶段语义映射**（配置）；阶段区间物化并携带出处

---

## 1. 核心分层：边缘报步序，中心做语义

模压机是按配方步序运行的。PLC / 配方控制器里有 step number 或 segment index —— **这是机器上的客观事实，不是推断**。

绝大多数团队的第一直觉是让边缘直接发 `phase = "anneal"`。这是错的，理由是它把**语义固化在了现场**：

| | 边缘报 `phase="anneal"` | 边缘报 `recipe_step=4` |
|---|---|---|
| 阶段划分要调整 | 改适配器，下车间，重启 | 改中心一张映射表 |
| 历史数据重新划分 | 不可能 | 重算即可 |
| 边缘需要知道 | 什么叫退火 | 现在是第几步 |
| 上报的是 | 一个解释 | 一个事实 |

**这与 README 的定位是同一条线：`Teams own source protocols. Ingot owns the fact contract.`** 边缘只报它确切知道的东西，语义归属留在中心，因为语义会变而事实不会。

```text
边缘（事实）                        中心（语义）
context.recipe_id      = "PGM-A17"
context.recipe_version = 3            PhaseMapping
context.recipe_step    = 4      ──►   (PGM-A17, v3, step 4) → anneal
```

配方版本参与映射键，所以配方改版后新老周期各按各的映射解释，**历史数据的阶段归属不会被追溯改写**。这一点对可审计性至关重要。

---

## 1.5 三个粒度不能混：配方、步序、阶段

一个配方**覆盖整个周期** —— 它是一整段程序，阶段是这段程序内部的时间结构。不存在"一个阶段一个配方"。三者粒度不同：

| 概念 | 粒度 | 例 | 存放位置 |
|---|---|---|---|
| `recipe_id` / `recipe_version` | **周期级常量**：这一炉跑哪个程序 | `PGM-A17 v3` | `context`，周期内不变 |
| `recipe_step` | **点级变量**：现在跑到第几段 | `1…12` | `context`，逐事件变化 |
| `PhaseDefinition` | **语义分组**：哪几段算一个阶段 | step 7,8,9 → anneal | 中心注册表 |

### 1.5.1 步 → 阶段是多对一

这是常态而非特例。真实的模压配方通常十几段，退火往往拆成多段不同速率：

```text
PGM-A17 v3
  step 1,2,3     → preheat
  step 4         → soak
  step 5,6       → press
  step 7,8,9     → anneal    ← 三段不同降温速率，同属一个阶段
  step 10,11,12  → cool
```

**阶段区间 = 连续同 phase 的步合并。** 物化时按 `occurred_at` 扫过步序变化点，相邻步映射到同一 phase 就并入同一区间。

`cycle_phases` 的主键含 `phase_order`，正是为了表达**同一 phase 在一个周期内出现多次且不连续**的情况（如二次压制 `press → anneal → press → anneal`）。不要用 `(correlation_id, phase_code)` 做主键。

### 1.5.2 映射键的分层 fallback（否则映射表会爆炸）

逐 `(recipe_id, recipe_version)` 配映射是不可行的：同一族配方的 v1/v2/v3 步序**结构完全一样**，只是温度压力参数不同。为每个版本重配一遍纯属浪费，且必然漏配。

映射按以下顺序查找，命中即止：

| 优先级 | 键 | 适用 |
|---|---|---|
| 1 | `(recipe_id, recipe_version, step)` | 特例覆盖：某个版本确实改了步序结构 |
| 2 | `(recipe_template, step)` | **实际主力**：同族配方共享步序结构，配一次全版本继承 |
| 3 | `step_name` 正则 | 控制器能报段名时（如 `ANNEAL_1` / `ANNEAL_2`），按命名约定自动归组 |
| 4 | 全局 `step → phase` 默认表 | 单一工艺产线，所有配方结构一致 |

因此 `context` 建议再加一个保留键 `recipe_template`（同族配方的结构标识）。若控制器能报段名，则 `recipe_step_name` 也值得采 —— 它让优先级 3 生效，映射维护量趋近于零。

**配置成本从 O(配方数 × 版本数) 降到 O(配方族数)，通常是几个而不是几百个。**

### 1.5.3 如果控制器根本不报 step

先确认一件事：**多数 PLC 里有 "current step / active segment" 寄存器，只是适配器没采。** 遇到这种情况第一反应应该是回去采，而不是立刻上推断 —— 采一个寄存器的成本，远低于维护一套推断规则。

确认拿不到时，按稳健度递减有三条路：

1. **注册配方的标称段结构（setpoint program）作为推断先验。** 配方定义本身包含每段的目标值和转换条件（"升温至 600 °C 到温转下一段"、"以 2 °C/min 降温至 300 °C"）。把这个结构注册进来，用转换条件去信号里标定边界 —— 找降温速率 ≈ 2 的连续区间。这是 model-based inference，比裸阈值稳健得多。
2. **纯信号规则推断**：温度阈值、斜率符号、压力信号有无。脆弱，随产品变化。
3. **归为 `unknown`**，只做整周期特征，放弃阶段级分析。

无论 1 还是 2，结果都是 `provenance = inferred`（见 §2），**不能因为方法更聪明就升级为事实**。区别只在推断质量，不在证据等级。

> 注意路线 1 有个反直觉的失效点：实际执行常常偏离标称时序 —— 保温段要等到温才转下一段，所以实际时长普遍长于标称。**因此绝不能用"周期起点 + 标称时间偏移"直接展开阶段边界**，必须用转换条件去信号里对齐。前者是最容易想到、也最容易错的做法。

---

## 2. 阶段来源的优先级链

现场情况参差不齐，必须有降级路径。但降级的结果**必须可区分**：

| 优先级 | 来源 | `provenance` | 说明 |
|---|---|---|---|
| 1 | 显式阶段事件 `phase.{code}.started` / `.completed` | `explicit_event` | 最强。边界精确到事件时刻 |
| 2 | 配方步序 + 映射表 | `recipe_step` | **推荐主路径**（§1） |
| 3 | 事件自带 `context.phase` 标注 | `event_tag` | 兼容已有系统 |
| 4 | 服务端规则推断（温度阈值、斜率符号、压力有无） | `inferred` | 降级，仅用于无步序的老设备 |
| 5 | 无法归属 | `unknown` | 计入数据质量问题，不静默丢弃 |

**这条链的价值不在于有几层，而在于第 4 层必须被标记出来。**

Ingot 的全部定位建立在"记录的是事实"上。推断出来的阶段不是事实，让它冒充观测结果，就是在事实库里掺沙子 —— 而且是最难发现的那种，因为它看起来和真的一模一样。

因此：
- `cycle_phases` 与 `cycle_features` 每行都带 `provenance`
- 基于 `inferred` 阶段算出的特征，工具结果必须附 `Limitation`
- Web 上视觉区分（虚线边界 / 灰色标注）
- Chat 回答引用这类数字时，限制说明不可省略

推断规则本身也要**版本化并落库**。否则半年后没人说得清"当时那批数据是按哪版规则划的阶段"，这个数字就失去了可复现性。

---

## 3. 物化：`cycle_phases`

不要在查询时实时推断阶段 —— 既贵又不可复现。物化成区间表：

```sql
cycle_phases (
  correlation_id        text        not null,
  phase_code            text        not null,
  phase_order           int         not null,
  started_at            timestamptz not null,
  ended_at              timestamptz,           -- null = 未闭合
  provenance            text        not null,  -- explicit_event|recipe_step|event_tag|inferred
  rule_version          int,                   -- provenance=inferred 时必填
  source_event_id_start uuid,                  -- 证据链
  source_event_id_end   uuid,
  is_complete           boolean     not null,
  computed_at           timestamptz not null,
  PRIMARY KEY (correlation_id, phase_code, phase_order)
)
```

有了这张表，特征计算退化成一个 join：

```sql
event.occurred_at >= phase.started_at AND event.occurred_at < phase.ended_at
```

`occurred_at` 是 hypertable 的分区键，这个 join 很便宜 —— 这正是 ADR-0004 §2「聚合下推、无行数上限」能成立的原因。

`source_event_id_start/end` 提供证据链：用户可以点开"凭什么说这段是退火"，看到具体是哪条事件划的界。**没有这两列，阶段就成了一个无法质疑的黑箱**，而 `BoundedInvestigationWorkflow` 里 challenge 角色最该攻击的就是这类隐含前提。

---

## 4. 边界语义（直接影响数字正确性）

`slope` 和 `integral` 对边界点极其敏感。必须明确规定，否则退火速率会因为实现细节的差异而给出不同答案。

### 4.1 半开区间

区间为 `[started_at, ended_at)`，前闭后开。切换时刻的采样点归入**后一个阶段**，不重复计入。

### 4.2 但斜率和积分需要跨界连续性

退火速率从退火开始那一刻算起，如果严格只用区间内的点，会漏掉起点温度，斜率算出来偏小。

因此 `FeatureDefinition` 增加：

```csharp
public string BoundaryMode { get; init; } = "strict";
// strict          仅区间内的点
// include_leading 纳入前一阶段的最后一点（slope / integral 默认）
// include_both    两端各纳入一点
```

`slope`、`integral`、`delta` 类聚合默认 `include_leading`；`min`/`max`/`mean` 类默认 `strict`。

这个细节很容易被跳过，但它决定了"同一段数据算出来的退火速率是否唯一"。**一个不能复现的数字，比没有数字更糟**。

### 4.3 阶段不允许重叠

当前阶段模型是线性序列。物化时检测到重叠即报数据质量问题，不做静默裁剪。并行阶段留到 ADR-0005 §8 的复审时点再议。

---

## 5. 乱序与迟到

`ProductionEvent` 已经分离了 `OccurredAt` 与 `RecordedAt`，边缘断网补传是设计内的正常情况。阶段物化必须相应处理：

- **一律以 `occurred_at` 排序**，不用摄入顺序。乱序到达不影响阶段划分结果。
- **周期封口后才物化**：收到周期完成事件，或超过配置的静默窗口（如 `occurred_at` 最新值 + 30 分钟无新事件）。
- **迟到事件触发重算**：若某周期已物化，之后又收到 `occurred_at` 落在其区间内的事件，标记该周期 `needs_recompute`，由后台作业重算 `cycle_phases` 和 `cycle_features`。
- **重算必须幂等**，且保留 `computed_at`，让"这个特征值是什么时候算出来的"可查。

未闭合的周期（`ended_at is null`）应当可被查询到，但在对比分析中默认排除，并在 `Limitations` 中声明排除了几个。**悄悄扔掉未完成的周期，会系统性地让良率看起来偏好** —— 因为异常中断的周期往往正是有问题的那些。

---

## 6. 阶段级数据质量检查

`PhaseDefinition` 已有 `Order` 和 `Required`，物化时顺带产出检查结果：

| 检查 | 说明 |
|---|---|
| 必需阶段缺失 | 退火段没有边界 = 该周期的退火速率不可算 |
| 顺序错乱 | 压制出现在退火之后 |
| 时长异常 | 超出 `PhaseDefinition` 的期望时长范围 |
| 时间空洞 | 周期内有事件不属于任何阶段（`unknown` 归属） |
| 出处降级 | 该周期有阶段来自 `inferred` |

这些直接扩展 `check_data_quality` —— 把现有的周期级配对检查提升到阶段级。事件类型建议采用 `phase.{code}.started` / `.completed` 命名，正是为了让现有的后缀配对逻辑（`.started` / `.completed` / `.cleared` / `.exited`）能直接复用，不必新写一套。

**这本身就是可独立交付的价值**（呼应 ADR-0002 §1.2）：在任何工艺分析开始之前，先告诉客户"你 12% 的周期退火阶段边界缺失，这些周期的内应力问题永远查不出来"。

---

## 7. 反模式

| 不要 | 原因 |
|---|---|
| 让边缘上报语义化的 `phase="anneal"` | 语义固化在现场，改一次要下车间；历史数据无法重新划分 |
| 把阈值推断作为主路径 | 脆弱、随产品变化、不可审计、污染事实定位 |
| 查询时实时推断阶段 | 不可复现（同一问题两次答案不同）、且贵 |
| 不区分 `inferred` 与观测 | 事实库掺沙子，且是最难发现的那种 |
| 静默丢弃无法归属的事件 | 归属失败本身就是重要的数据质量信号 |
| 对比分析时静默排除未闭合周期 | 异常中断的周期往往正是有问题的那些，排除会让良率系统性偏好 |

---

## 8. 落地顺序

| 步骤 | 内容 | 依赖 |
|---|---|---|
| 1 | RFC 增加 `recipe_id` / `recipe_version` / `recipe_template` / `recipe_step` / `recipe_step_name` 保留键 | 无，纯文档 |
| 2 | `PhaseDefinition` + `PhaseMapping` 注册表（与 ADR-0005 特征注册表同一套主数据） | 1 |
| 3 | `cycle_phases` 物化 + 封口/重算作业 | 2 |
| 4 | `BoundaryMode` 进 `FeatureDefinition`，`cycle_features` 按阶段计算 | 3 |
| 5 | `check_data_quality` 扩展阶段级检查 | 3 |
| 6 | `inferred` 推断规则引擎（仅在遇到无步序的老设备时才做） | 3，可延后 |

步骤 6 刻意排在最后：**先把有步序的设备做好，不要一开始就为最差的数据源设计**。

---

## 9. 权衡

| 决策 | 收益 | 代价 |
|---|---|---|
| 步序 → 阶段的中心映射 | 语义可改、历史可重划、边缘极简 | 依赖边缘能拿到 step number；拿不到时要走 `inferred` |
| 物化 `cycle_phases` | 特征计算变成廉价 join | 存储与重算作业；迟到事件带来一致性窗口 |
| `provenance` 贯穿到底 | 守住事实定位 | 每一层（存储、工具、UI、回答）都要传递和呈现，容易漏 |
| `BoundaryMode` 可配置 | 斜率/积分数值正确且唯一 | 增加配置认知负担；默认值必须选对，否则多数人不会改 |
| 未闭合周期不静默排除 | 避免良率偏差 | 用户会看到更多 limitation，观感上"系统很啰嗦" |

---

## 10. 复审时点

- **接入第一台没有 step number 的设备时**：推断规则引擎的形态（配置化表达式 vs 代码插件）。
- **出现并行 / 嵌套阶段的工艺时**：线性阶段模型需要升级。
- **迟到事件重算量变大时**：是否需要把重算从后台作业改为增量流式。
- **`unknown` 归属比例长期高于 5% 时**：说明保留键规范或适配器实现有系统性问题，而不是个别数据问题。

---

参见 [ADR-0004](adr-0004-cross-cycle-analysis.md)、[ADR-0005](adr-0005-feature-registry.md)、[生产事件规范](../rfc-production-events.md)。
