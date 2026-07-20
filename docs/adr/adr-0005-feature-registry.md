# ADR-0005：过程特征注册表与光学镜片模压首套特征集

**状态（Status）：** Proposed
**日期（Date）：** 2026-07-20
**决策者（Deciders）：** Ingot 维护者（@liuweichaox）
**依赖：** [ADR-0004](adr-0004-cross-cycle-analysis.md) 的 `cycles` / `cycle_features` 物化层
**决策：** 特征采用**配置化注册表**；首个落地工艺为**光学镜片模压**

> **需要工艺侧确认**：本文 §4 的特征集基于精密玻璃模压的通用工程认知给出，用于确立注册表的表达能力边界，**不是可直接投产的工艺参数**。实际的阶段划分、关键参数和限值必须由工艺工程师核定。请把它当作"注册表能不能表达这些"的验证用例来读。

---

## 1. 为什么是注册表，而不是硬编码

光学镜片模压这个选择本身就否决了硬编码方案。理由不是"以后可能要扩展"，而是这个工艺有三个结构性特点：

**其一，特征必须绑定到阶段，整周期统计量基本无意义。** 一个模压周期跨越预热、均热、压制、退火、冷却五个阶段，温度在其中走完一个完整的升降。"整周期平均模具温度"是一个没有任何工艺含义的数字；有含义的是"压制阶段的峰值温度"和"退火阶段的降温速率"。

**这一点直接改变了 `cycle_features` 的形状 —— 必须有 phase 维度。** 硬编码方案通常会漏掉这一维，等发现时物化表已经落地，回填代价很高。

**其二，关键特征是形状量而非位置量。** 退火速率（斜率）、保压冲量（积分）、均热驻留时长（阈值以上停留时间）—— 这些都不是 min/max/mean 能表达的。聚合方式本身必须是可配置的枚举，而不是一组固定的统计函数。

**其三，模具是一等自变量。** 同一台设备、同一配方、同一批预制件，换一副模具或多压两万模次，面形就会漂。任何不能按 `mold_id` 和累计模次分组的分析，在这个工艺里都是错的。

---

## 2. 注册表契约

### 2.1 阶段定义

```csharp
public sealed record PhaseDefinition
{
    public required string PhaseCode { get; init; }        // preheat | soak | press | anneal | cool
    public required int Order { get; init; }
    public required string DisplayName { get; init; }
    public required string StartEventType { get; init; }   // phase.press.started
    public required string EndEventType { get; init; }     // phase.press.completed
    public required string OperationCode { get; init; }    // 阶段划分随工序而变
    public bool Required { get; init; } = true;            // 缺失时是否记为数据不完整
}
```

阶段边界由**事件类型**界定，与现有 `.started` / `.completed` / `.exited` 约定一致（`CheckDataQualityTool` 已在用这套后缀）。建议事件类型采用 `phase.{phaseCode}.started` / `.completed` 命名，这样现有的周期配对检查逻辑可以直接复用到阶段级完整性检查上，不需要新代码。

`Required` 的用处：退火阶段缺失是严重的数据问题，而冷却阶段可能本就不采集。二者不能用同一套告警。

### 2.2 特征定义

```csharp
public sealed record FeatureDefinition
{
    public required string FeatureCode { get; init; }      // anneal.rate_c_per_min
    public required int Version { get; init; }
    public required string DisplayName { get; init; }
    public string? Unit { get; init; }                     // UCUM，与 InspectionRecord 同源

    // 适用范围
    public IReadOnlyList<string> OperationCodes { get; init; } = [];
    public IReadOnlyList<string> ProductCodes { get; init; } = [];
    public string? PhaseCode { get; init; }                // null = 整周期

    // 取值来源
    public required FeatureSource Source { get; init; }

    // 聚合方式
    public required string Aggregation { get; init; }
    public FeatureAggregationArgs? Args { get; init; }

    // 期望范围：用于标记「偏离预期」，不是质量判定
    public decimal? ExpectedLower { get; init; }
    public decimal? ExpectedUpper { get; init; }
    public decimal? Target { get; init; }
}

public sealed record FeatureSource
{
    public required string Kind { get; init; }        // event_data | event_timing | context | derived
    public string? JsonPath { get; init; }            // $.mold_temp_upper_c
    public string? EventType { get; init; }           // 限定只取某类事件
    public string? Expression { get; init; }          // kind=derived 时，引用其它 featureCode
}
```

**聚合方式枚举**（决定注册表的表达能力上限）：

| 聚合 | 含义 | 光学模压用例 |
|---|---|---|
| `min` `max` `mean` `p50` `p95` `stddev` | 基础统计 | 通用 |
| `first` `last` `delta` | 阶段首值 / 末值 / 净变化 | 退火起止温差 |
| `slope` | 线性拟合斜率 | **退火速率** |
| `slope_deviation` | 与目标斜率的最大偏离 | 退火速率稳定性 |
| `integral` | 时间积分 | **保压冲量**、等效热负荷 |
| `dwell` | 满足条件的累计时长（`Args.Threshold` / `Comparison`） | 均热驻留 |
| `range_across` | 同时刻多路信号的极差（`Args.Paths`） | **上下模温差** |
| `count` | 计数 | 报警次数 |
| `duration` | 阶段时长 | 通用 |

`slope`、`integral`、`dwell`、`range_across` 这四个是这个工艺真正需要、而通用统计集给不了的。它们也是判断"注册表设计够不够"的试金石。

### 2.3 物化表

```sql
cycle_features (
  correlation_id   text,
  phase_code       text,        -- '' 表示整周期
  feature_code     text,
  feature_version  int,
  value            numeric,
  sample_count     int,         -- 参与计算的原始点数
  computed_at      timestamptz,
  PRIMARY KEY (correlation_id, phase_code, feature_code)
)
```

`sample_count` 是必需的：斜率由 3 个点拟合还是 300 个点拟合，可信度完全不同。这个数字必须能进 `Limitations`，否则 agent 会把噪声当趋势讲。

**这一列是 ADR-0004 §1"全量数据参与计算，但不参与推理"的具体兑现物** —— 模型看到 `value` 和 `sample_count`，知道这个数字背后有多少原始数据支撑，但那些数据本身从不进入上下文。

---

## 3. 需要补充的 context 保留键

在 ADR-0004 §3.1 的基础上，光学模压追加：

| 键 | 说明 | 为什么关键 |
|---|---|---|
| `mold_id` | 模具唯一标识 | **面形漂移的第一自变量** |
| `mold_shot_count` | 该模具累计模次 | 镀层退化的代理变量 |
| `preform_lot` | 玻璃预制件批次 | 材料波动的分组维度 |
| `cavity_id` | 型腔号（多腔模） | 腔间差异是常见系统性偏差 |
| `recipe_id` / `recipe_version` | 配方及版本 | 配方变更是最常见的真因 |

`mold_id` + `mold_shot_count` 这一对，价值高于其余所有键的总和。它们让"模具寿命"从经验判断变成可计算的曲线。

---

## 4. 首套特征集（待工艺核定）

### 4.1 阶段

```text
preheat（预热）→ soak（均热）→ press（压制）→ anneal（退火）→ cool（冷却）
```

### 4.2 通用特征（工艺无关，随注册表内置）

`cycle.duration_ms`、`cycle.event_count`、`phase.{code}.duration_ms`，以及对任意数值型 `data` 字段自动生成的 `min/max/mean/p50/p95/stddev`。

自动生成的这批是**兜底**：客户接入当天，还没人配任何特征定义，也应该能做基本对比。注册表用于表达"有工艺含义"的那些。

### 4.3 光学模压专用特征

| featureCode | 阶段 | 聚合 | 工艺含义 |
|---|---|---|---|
| `mold.temp.peak_c` | press | max | 峰值模温，影响面形复制完整度 |
| `mold.temp.uniformity_c` | press | range_across（上/下模） | **上下模温差 → 偏心与面形不对称** |
| `soak.dwell_above_tg_ms` | soak | dwell(≥ Tg) | 均热不足则玻璃流动不充分 |
| `press.force_peak_n` | press | max | 压制力峰值 |
| `press.force_impulse` | press | integral | **保压冲量，比峰值更能预测复制精度** |
| `press.rate_mm_per_s` | press | slope | 压制速度影响内应力分布 |
| `anneal.rate_c_per_min` | anneal | slope | **最关键 —— 决定内应力与折射率均匀性** |
| `anneal.rate_deviation` | anneal | slope_deviation | 退火速率的稳定性，通常比均值更能解释不良 |
| `cool.rate_c_per_min` | cool | slope | 急冷导致的残余应力 |
| `atmosphere.o2_ppm_max` | 整周期 | max | 氧含量 → 模具镀层氧化，直接吃模具寿命 |
| `thermal.budget` | 整周期 | integral(T·dt) | 等效热负荷，模具退化的累积指标 |

### 4.4 对应的质量特性（`InspectionDefinition`，ADR-0002）

面形精度 PV / RMS、中心厚度、偏心、表面粗糙度、条纹与气泡计数。

**过程特征与质量特性必须能对齐到同一个周期** —— 这就是 ADR-0004 §3.3 那个阻断项（`operationRunId` ↔ `correlationId`）在本工艺下的具体形态。没有它，上面 11 个特征全部只能自说自话。

---

## 5. 这套设计要支撑的四个分析场景

特征集的对错，用能否回答这些问题来检验：

1. **模具寿命漂移** —— 固定 `mold_id`，看面形 PV 随 `mold_shot_count` 的趋势，预测何时该下模修复镀层。这是光学模压最贵的单一问题，也是本设计最有可能创造直接经济价值的地方。
2. **退火速率一致性 → 内应力不良** —— `anneal.rate_deviation` 分组对比合格与不合格批次。
3. **上下模温差 → 偏心** —— `mold.temp.uniformity_c` 与偏心测量值的相关性，按 `cavity_id` 分层。
4. **预制件批次 → 良率** —— 按 `preform_lot` 分组的合格率对比。

四个场景全部是 ADR-0004 的 `find_comparable_cycles` + `compare_cycles` 的直接用例，分组维度分别是 `mold_id`、`recipe_version`、`cavity_id`、`preform_lot`。**这四个键必须进 context 保留键规范**，否则工具能力再强也分不了组。

---

## 6. 这个工艺带来的两个意外结论

### 6.1 降采样和异步作业的优先级可以降低

光学模压周期长（分钟级）、采样率不高、日周期数有限（相比大批量机加工低 2–3 个数量级）。一条完整曲线可能只有几百到几千点。

**这意味着"完整曲线直接进 `Artifacts` 给人看"完全可行，甚至可以在前端叠画十几条曲线做形状对比 —— 不需要降采样。** ADR-0004 §5 的异步分析作业在这个场景下不是必需品，可以推后。

反过来说，这个工艺是验证整套架构的理想首站：数据量温和，但分析深度要求高。

### 6.2 单件价值高，改变了成本权衡

一片高精度模压镜片的价值，远高于扫一遍数据库的计算成本。ADR-0004 §6 的扫描预算护栏在这里可以定得很宽松 —— **在这个工艺上，宁可多算，不可漏判**。

护栏仍然要建（换到大批量工艺时必须能收紧），但初始阈值不必保守。

---

## 7. 权衡

| 决策 | 收益 | 代价 |
|---|---|---|
| 配置化注册表 | 适配多工艺；特征可随认知演进 | 需要定义管理界面、版本与回填机制；首个客户上线前要有人把定义配出来 |
| 特征绑定 phase | 光学模压唯一可行的形态 | 依赖适配器正确发出阶段事件；阶段缺失需要明确的降级语义 |
| 保留自动生成的通用特征 | 零配置也能用 | 与注册表特征混在同一张表，需要 `is_auto` 标记区分，避免自动特征污染工艺分析 |
| `slope` / `integral` / `dwell` / `range_across` 进枚举 | 表达力够用 | 这四个的 SQL 下推实现比基础统计复杂得多，是本 ADR 主要工程量所在 |
| 期望范围只标记不判定 | 守住"Ingot 不做质量处置"的定位 | 用户会追问"那到底合不合格"，需要在 UI 上明确指向 QMS |

---

## 8. 复审时点

- **接入第二个工艺时**：阶段模型是否需要支持嵌套或并行阶段（当前是线性序列）。
- **特征定义超过 100 条时**：是否需要产品族继承，避免每个产品重复配置。
- **`slope` 类特征出现争议时**：线性拟合可能不够，需要引入分段拟合或指定拟合窗口。这在退火速率上很可能发生 —— 实际退火曲线常常不是一条直线。
- **模具寿命预测被真实使用后**：是否要从"看趋势"升级为 ADR-0003 §3.3 的时序模型预测。

---

参见 [ADR-0002](adr-0002-manual-inspection-input.md)、[ADR-0003](adr-0003-capability-amplification-roadmap.md)、[ADR-0004](adr-0004-cross-cycle-analysis.md)。
