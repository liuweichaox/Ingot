# ADR-0004：跨周期工艺对比与"全量分析"的正确实现

**状态（Status）：** Proposed
**日期（Date）：** 2026-07-20
**决策者（Deciders）：** Ingot 维护者（@liuweichaox）
**触发：** 工艺分析需要拿一批历史同类周期做对比，且分析不应被行数上限截断。

---

## 1. 需求成立，但有一个必须先拆开的混淆

**需求成立**：工艺分析的本质就是比较 —— 这批与上批、这台与那台、换料前与换料后。只能看单个 `correlationId` 的系统做不了工艺分析。这是产品级缺口，不是优化项。

**但"不受行数约束、加载每个周期的所有数据"这句话里，藏着两件必须分开的事：**

| | 现状 | 应该是 |
|---|---|---|
| **计算范围**（有多少数据参与运算） | 被 `Limit = 500` 截断 | **无上限，全量** |
| **返回载荷**（进入模型关联信息的字节） | 同样是那 500 行 | **严格有界的统计摘要** |

现在这两件事被同一个 `Limit` 绑在一起，这才是真正的设计缺陷。把 `Limit` 从 500 提到 500000 会同时放开两者 —— 计算范围对了，返回载荷灾难：

- 10 个周期 × 每周期数千条事件 = 数十万 token，成本与延迟不可接受；
- 更致命的是，**这等于让模型自己从原始行里做算术**。而 `IAnalysisResultValidator` 存在的全部意义，就是防止答案里的数字来自模型而不是来自确定性计算。给模型灌全量明细，是从架构内部拆掉自己最值钱的那道墙。

**所以正确的说法是：全量数据必须参与计算，但不参与推理。**

### 1.1 代码里已经有正确答案的雏形

`CheckDataQualityTool` 其实已经示范了这个模式，只是没有被总结成原则：

```csharp
// 全范围聚合：总条数与新鲜度不受 500 明细窗口截断
var stats = await events.GetScopeStatsAsync(context.UserId, scope, ct);
// 明细窗口：周期配对 / 序号连续性 / 生产信息缺失需要逐行数据，仍限 500 行
var rows = await events.QueryAsync(context.UserId, scope with { Limit = 500 }, ct);
```

**本 ADR 要做的，就是把这个模式从一个工具里的局部处理，提升为全平台的架构原则。**

---

## 2. 决策：三条原则

### 原则一：能下推的聚合一律下推，且无行数上限

凡是能用 SQL / TimescaleDB 表达的统计（计数、分位数、均值、标准差、时间桶、分组对比、缺口检测），一律在数据库里算，扫多少行都可以。C# 侧只接收结果。

只有**必须逐行、且无法用 SQL 表达**的检查才使用有界窗口，并且必须在 `Limitations` 里显式声明窗口边界 —— 这一点现有代码已经做对了，保持。

判定标准很简单：**如果一个统计量需要先把行拉到 C# 再 LINQ 聚合，那就是设计错了**，除非它确实无法下推。

### 原则二：`Data` 有界给模型，`Details` 全量给人

`AnalysisToolResult` 当前只有一个 `Data`，直接进模型关联信息。需要拆成三层：

```csharp
public sealed record AnalysisToolResult
{
    public required string Summary { get; init; }        // 自然语言摘要
    public required JsonElement Data { get; init; }      // 给模型：统计量，硬上限（建议 32 KB）
    public IReadOnlyList<ResultDetail> Details { get; init; } = [];  // 给人：全量结果、图表、CSV
    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];        // 指向全量的可验证链接
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public string Outcome { get; init; }
}

public sealed record ResultDetail
{
    public required string DetailId { get; init; }   // UUIDv7
    public required string Kind { get; init; }         // table | series | chart-spec | csv
    public required string Label { get; init; }
    public required string Url { get; init; }          // 前端拉取，不进模型关联信息
    public required long RowCount { get; init; }
}
```

**模型看摘要，人看全量，原始记录链接指向全量。** 这样既保留分析能力，也方便用户核对数字——用户在 Web 上能点开完整的 8000 行对比明细和曲线，模型只接收“P95 = 58.3，n = 1240”这类已经算好且可重新计算核对的数字。

在 `Data` 上加硬字节上限，并在超限时截断 + 记 limitation。这个上限应该由 `DefaultPlanValidator` / `AgentGuards` 强制，而不是靠每个工具自觉。

### 原则三：周期是一等公民，必须物化

当前架构是**事件级**的，工艺分析是**周期级**的。中间缺一层。

```text
production_events (hypertable, 明细)
   └─ 连续聚合 / 物化 ─────► cycles           每周期一行
   │                          correlation_id, subject_id, edge_id,
   │                          started_at, completed_at, duration_ms,
   │                          event_count, context_snapshot(JSONB), is_complete
   │
   └─ 连续聚合 / 物化 ─────► cycle_features   每周期每特征一行
                              correlation_id, feature_code,
                              min, max, mean, p50, p95, stddev,
                              slope, dwell_ms, sample_count
```

对比 10 个周期 = 读 10 行 `cycles` + 几十行 `cycle_features` = 几 KB。而这几 KB 背后是**全量明细扫过一遍**得到的结果 —— 物化的时候扫过。这正是"加载每个周期的所有数据进行分析"的正确实现方式：全量被计算了，只是没有被搬运。

ADR-0001 已经迁到 TimescaleDB hypertable，continuous aggregate 正是为这件事准备的。

---

## 3. 前置：什么叫"同类"

在写任何对比工具之前，必须先能判定两个周期是否可比。否则工具只能退化成"用户自己传一串 correlationId"，等于没做。

### 3.1 短期：规范化 context 保留键

批次信息目前只能塞进自由 KV `Context`（`EventsView` 的占位符 `material_lot` / `LOT-001` 说明这是既定用法）。底层 `EventFilter.Context` 已支持按键值过滤 —— 通道是通的，缺的是**约定**。

在 RFC 中定义保留键，适配器必须填写：

| 保留键 | 含义 | 用途 |
|---|---|---|
| `product_code` | 产品 / 零件号 | 同类判定的第一维 |
| `operation_code` | 工序编码 | 同类判定的第二维 |
| `recipe_id` / `recipe_version` | 工艺配方及版本 | 配方变更是最常见的真因 |
| `material_lot` | 原料批次 | 分组对比的主要自变量 |
| `work_order` | 工单 | 业务侧回溯 |

这是**最低成本、最高回报**的一步：不改任何存储结构，只是把口径写进规范。没有它，A 线写 `material_lot`、B 线写 `lotId`，跨线对比永远做不成。

### 3.2 中期：含义层

ADR-0003 §4.3 的产品族 / 工艺路线本体，让"同类"可以是推导出来的（同产品族、同工艺路线、同模具），而不只是字符串相等。**先做 3.1，含义层在其上生长**。

### 3.3 必须先修的断裂：过程与质量没有关联

`ProductionEvent` 用 `correlationId`，`InspectionRecord` 用 `operationRunId`。走查确认：`operationRunId` 在检测模块之外的代码里出现 **0 次**，RFC 里也没有定义两者关系。

**工艺分析的本质是"参数 → 结果"。这座桥不存在，跨周期对比就只能比过程不能比结果**，价值直接减半 —— 没人关心"这批温度高了 3 度"，他们关心"温度高了 3 度所以硬度不合格"。

建议：在 RFC 中明确 `operationRunId` 即该次加工运行的 `correlationId`（或提供显式映射），并在 `cycles` 物化中带上该周期的检测结论汇总（合格数 / 不良数 / 关键特性均值）。

**这是本 ADR 里唯一一个"不修就无法交付价值"的阻断项。**

---

## 4. 新增工具

### 4.1 `find_comparable_cycles` —— 检索，与对比分离

```jsonc
{
  "anchor": { "correlationId": "CYCLE-001" },   // 或直接给筛选条件
  "matchOn": ["product_code", "operation_code", "recipe_id"],
  "window": { "from": "...", "to": "..." },
  "limit": 200                                   // 返回周期条数，非事件行数
}
```

返回：匹配的周期摘要列表 + **采用的对比条件**（哪些键相等、哪些不等）。

把“找同类”独立成查询功能，是为了让“为什么把它们放在一起比较”这件事**看得见、能复核、可修正**。这正是 `BoundedCombinedAnalysisWorkflow` 的复核视角应该检查的地方。对比条件不能藏在查询功能内部。

### 4.2 `compare_cycles` —— 对比

```jsonc
{
  "groupA": { "correlationIds": [...] },        // 或筛选条件
  "groupB": { "correlationIds": [...] },        // 可省略，则与 A 的历史基线比
  "features": ["duration_ms", "temp_peak", "hardness"]
}
```

返回（全部由 SQL 下推计算，无行数上限）：

- 每个特征在两组中的 **n / mean / p50 / p95 / stddev / 分布直方图桶**
- **效应量（Cohen's d 或等价指标）与置信区间 —— 不要只给 p 值**，样本一大 p 值必然显著，会持续误导
- 事件序列差异（哪些事件类型只在一组出现、顺序差异）
- 检测结论对比（合格率、关键特性偏移）
- `Details`：完整的逐周期明细表 + 可下载 CSV

**设计要点**：默认返回统计量，不返回逐周期明细。明细放 `Details`。

### 4.3 `get_cycle_trace` 的兼容扩展

保持单周期含义不变（它是"看明细"的工具，500 行上限合理）。跨周期由上面两个新工具承担。**不要把 `get_cycle_trace` 改成支持数组** —— 那会让一个工具同时承担"看明细"和"做对比"两种含义，输入 schema 和返回形状都会退化。

---

## 5. 真正需要原始序列的场景

有些分析确实需要逐点数据，比如"这两批的升温曲线形状哪里不一样"。三条出路，按顺序尝试：

1. **降采样后对比**：在 DB 里做时间桶或 LTTB 降采样到 200 点以内，返回两条曲线 + 差异区间标注。**降采样在数据库做，不在 C# 做**。人眼和模型都不需要 10 万个点。
2. **异步分析作业**：不在 Chat 的同步回合里跑。agent 提交作业 → 后端全量计算（允许几十秒）→ 结果回流。SSE 基础设施已存在，进度可以流式推。这是"分析不受行数约束"的最终答案。
3. **时序基础模型编码**（ADR-0003 §3.3）：把整段序列编码成向量 / 特征再进对话。**原始波形本来就不该进 LLM 关联信息** —— 让通用对话模型逐点读时序数据，既贵又不可靠。

---

## 6. 成本护栏（不能省）

"无行数上限"不等于"免费"。一个宽泛的问题可以扫全库。必须在 `AgentGuards` 层增加：

- **扫描预算**：预估扫描行数 / 时间分区数，超过阈值先返回"范围过宽，请收窄或转异步作业"，而不是硬跑
- **周期条数上限**：单次对比的周期数有上限（如 500），超过则要求抽样并在 limitation 中声明抽样方式
- **物化优先**：能命中 `cycles` / `cycle_features` 的查询绝不下沉到明细表
- **超时降级**：超时返回部分结果 + 明确的 limitation，而不是失败或静默截断

这些护栏和现有的 `MaxToolCalls`、轮数上限属于同一类设计 —— **有界性是这个平台的特性，不是限制**。放开行数上限的同时，必须补上等价的资源边界，否则只是把不可控从关联信息长度转移到了数据库负载。

---

## 7. 落地顺序

| 步骤 | 内容 | 为什么在这个位置 |
|---|---|---|
| **1** | RFC 定义 context 保留键 | 零代码改动，但决定后面一切能否成立 |
| **2** | 打通 `operationRunId` ↔ `correlationId` | §3.3 的阻断项，不修则只能比过程不能比结果 |
| **3** | 物化 `cycles` / `cycle_features` | 全量计算的执行层 |
| **4** | `AnalysisToolResult` 拆出 `Details` + `Data` 字节上限 | 契约变更，越早越好；同时立即约束现有工具 |
| **5** | `find_comparable_cycles` + `compare_cycles` | 用户可感知的能力交付 |
| **6** | `AgentGuards` 扫描预算与周期数上限 | 必须与 5 同期上线，不能延后 |
| **7** | 异步分析作业 功能入口 | 承接需要原始序列的重分析 |

步骤 1–2 是文档和契约工作，可以立刻开始，且不阻塞 3–4 的并行推进。

---

## 8. 权衡（Trade-offs）

| 决策 | 收益 | 代价 |
|---|---|---|
| 聚合下推到 DB | 全量计算、载荷可控、数字可验证 | 逻辑从 C#（易测）转移到 SQL（难测）；需要为 SQL 聚合补测试 |
| 物化 cycles / cycle_features | 对比查询变廉价 | 存储放大；特征集变更需要回填；物化延迟带来"最新周期尚未入表"的边界情况 |
| `Data` / `Details` 分离 | 模型关联信息可控且不做算术 | 契约破坏性变更；前端要新增 明细文件 拉取与渲染 |
| 严格的 context 保留键 | 跨源可比 | 对接入方是硬性约束，提高了接入门槛 —— 需要用 ADR-0003 §3.2 的映射辅助来抵消 |
| 拒绝把明细灌进模型 | 守住数字接地这条底线 | 少数"让模型自由发挥看原始数据"的场景做不了 —— 这是**有意**放弃的 |

---

## 9. 复审时点

- **单次对比周期数常态超过 500 时**：抽样策略是否需要从随机改为分层。
- **`cycle_features` 特征集第三次变更时**：说明特征应该可配置而非硬编码，需要特征定义注册表（与 ADR-0002 的 `InspectionDefinition` 合并考虑）。
- **异步作业成为主要路径时**：是否需要独立的计算服务，而不是跑在 Platform API 进程内。

---

参见 [ADR-0001](adr-0001-local-production-record-store.md)、[ADR-0002](adr-0002-manual-inspection-input.md)、[ADR-0003](adr-0003-capability-amplification-roadmap.md)、[生产事件规范](../rfc-production-events.md)。
