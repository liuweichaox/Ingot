# 光学玻璃镜片模压：配置化样例

这个样例把设备专有字段留在行业配置中，Ingot 核心只认识事件信封、Profile 引用、稳定代码和值。用户给出的中文字段不会成为平台通用表的固定列。

## 样例覆盖范围

- 一个模压周期 10 分钟，共 600 秒；
- 每秒一次原子扫描，每条 `process.sample` 同时携带完整的 13 个传感器值；
- 31 个设备专有配方参数，支持沿用上一版后局部修改；四个物理模具部件不属于配方；
- 开始生产时生成不可变的全量配方快照，不要求记录参数修改原因；
- 5 个模压阶段，由工艺数据模型中的版本化步序映射解释；
- 视觉检测长期保存原始图片，并通过 SHA-256 支持复核；
- 人工质检独立记录；
- 支持完整周期事件查询以及同产品系列的历史周期对比。

这次实际提供的是 **13 个**传感器，不是之前讨论的 10 个。Profile 可按设备版本增减或停用字段，核心模型无需变更。

采集模型只保留接入与解释原始数据所需的信息：稳定 `code`、设备 `sourceField`、类型、单位和数据类别。哪些信号参与比较、如何对齐以及按什么质量结果分组，属于独立的分析方案，不进入采集数据项定义。`quantityKind`、通用 `enabled`、恒等 `transform` 和没有发生单位换算时重复的 `sourceUnit/canonicalUnit` 均不进入本样例；将来只有出现真实需求时才增加相应配置。

## 文件

| 文件 | 用途 |
| --- | --- |
| `acquisition-profile.v1.json` | 13 个原始传感器字段到稳定代码的映射，声明 1 Hz 原子成组采样 |
| `recipe-profile.v1.json` | 31 个配方参数的类型、单位和稳定代码 |
| `recipe-instance.example.json` | 从 v6 复制、修改 3 项后形成的 v7 全量配方 |
| `phase-mapping.v1.json` | 控制器步序到 5 个业务阶段的版本化映射 |
| `process-analysis-plan.v1.json` | 周期分析范围、阶段对齐、质量分组和 5 个比较数据项 |
| `vision-inspection.example.json` | 带长期保存原图引用和哈希的视觉检测记录 |
| `manual-inspection.example.json` | 人工终检记录 |
| `generate-cycle.mjs` | 生成完整 600 秒、608 事件的可重现周期 |
| `generate-factory-day.mjs` | 生成 2 台设备、3 个产品系列、8 小时的完整单日模拟数据 |
| `import-factory-day.mjs` | 把行业模板、单日生产事件、原图和检测记录幂等导入 Platform API |

## 运行样例

仓库要求的 Node.js 22 可直接运行：

```powershell
node docs/samples/optical-molding-flexible/generate-cycle.mjs
```

输出写到忽略版本控制的 `generated/`：

- `event-batch-001.json`：500 条事件；
- `event-batch-002.json`：108 条事件；
- `recipe-applied.example.json`：实际应用的完整配方快照事件；
- `process-sample.example.json`：第 240 秒的一组 13 值采样；
- `summary.json`：周期数量校验和配方快照 SHA-256。

批次的 500 只是 Edge 到 Platform 的单次传输大小，不是周期查询上限。两个批次使用相同 `correlationId`，周期接口应返回全部 608 条事件；Web 时间线可以虚拟渲染，但不能静默截断。

## 数据组织

```text
设备原始字段
  └─ Acquisition Profile v1 ──> process.sample.data.values

基础配方 v6 + 本次 3 项覆盖
  └─ Recipe Profile v1 ──> v7 resolvedParameters ──> recipe.applied

recipe_step + Process Data Model v1
  └─> preheat / soak / press / anneal / cool

生产周期
  ├─> 600 组过程值
  ├─> 视觉检测 ──> 原图对象存储 + 哈希
  └─> 人工质检
```

平台数据库不需要为这些字段创建 48 个行业专有列。建议分别保存：

1. 不可变 Profile 版本及其 JSON 定义；
2. 配方主数据、版本关系和全量参数快照；
3. 通用 `production_event`，传感器组位于 `data.values`；
4. 检测记录元数据；
5. 原始视觉图片存入具备长期保留策略的对象存储，数据库只保存受控引用、哈希、媒体类型和大小。

Profile 发布后不可原地修改。字段变化时发布 v2，事件继续引用采集时生效的版本，历史数据因此始终可解释。

## 单日工厂模拟

默认模拟日期为 2026-07-20，两台设备从 08:00 到 16:00 并行生产：

```powershell
node docs/samples/optical-molding-flexible/generate-factory-day.mjs
```

默认输出目录是 `generated/factory-day-2026-07-20/`，包含：

- 2 台设备各 48 个周期，共 96 个完整周期；
- LENS-A、LENS-B、LENS-C 各 32 个同系列历史周期；
- 57,600 组秒级原子采样和 58,368 条生产事件；
- 每周期 1 条视觉检查、1 条人工终检和 1 张可上传复核的 BMP 原图；
- 少量确定性的温度超调、真空漂移、压力漂移及不合格结果；
- `summary.json`、`cycles.json`、500 条一页的事件批次及质检导入清单。
- `inspection-plans.json` 和 `inspection-definitions.json` 定义质量要求；工艺阶段来自工艺数据模型，历史比较信号来自分析方案，不再维护第二套阶段、映射和特征配置。
- `process-data-models.json`、6 个完整的 `recipe-versions.json` 配方版本和 `process-analysis-plans.json`，分别保存定义、实际配方值和分析选择。
- `manufacturing-setup.json`，包含配置化工装类型、12 个物理组件、3 个模具组合版本，以及每个周期对应的装模区间和生产上下文。

输出目录必须为空，避免改变日期或时长后残留旧批次。可用 `--date`、`--hours` 和 `--out` 覆盖默认值。导入运行中的 Platform API：

```powershell
$env:INGOT_EDGE_TOKEN='your-edge-service-token'
$env:INGOT_PLATFORM_TOKEN='your-platform-identity-token'
node docs/samples/optical-molding-flexible/import-factory-day.mjs
```

Edge token 是设备摄入服务凭据；Platform token 由统一认证提供，页面不再设置额外用户名或访问密码。导入器先发布光学模压行业模板中的质量方案、检测定义、工艺数据模型、完整配方版本、分析方案和生产准备数据，再导入事件与检测记录；Platform 不会自动创建这些行业规则。稳定标识支持安全重跑。也可以用 `--api`、`--dir`、`--skip-events` 或 `--skip-inspections` 控制导入范围。

## 配方语义

`recipe-instance.example.json` 同时保存：

- `basedOn`：沿用的上一版；
- `overrides`：本次实际修改的参数，仅用于界面展示；
- `resolvedParameters`：本周期真正使用的全部 31 个工艺参数，是配方追溯的权威快照。

上模芯、下模芯、上模架和下模架由 `manufacturing-setup.json` 的模具组合版本管理。组件更换创建新的组合版本，不能通过修改配方参数改写历史。

没有 `changeReason`。即使操作者只修改一个参数，也必须在 `recipe.applied` 中冻结全量解析结果并计算快照哈希，不能在历史查询时再动态继承旧配方。

## 实施前语义核对（不进入数据模型）

明确带 `℃`、`s`、`mm`、`mm/s` 的字段已使用 UCUM 单位。下列信息不能从字段名可靠推断，应在项目实施时核对，但“已确认/待确认”是实施过程状态，不是工业数据语义，因此不保存到 Profile：

- `压力kg` 以及所有以 kg 表示的压力项，需确认是 kgf、质量读数还是控制器工程量；
- `充氮气温度`、保压温度、WORK 温度、模压温度；
- 上下模断电延时；
- 保压压力、保压速度；
- PID 参数的量纲及算法形式；
- 真空度的压力基准是绝压还是表压。

核对后直接配置正确单位或换算；若已经有生产数据使用旧定义，则发布新的 Profile 版本，不能修改旧版本来重新解释历史数据。

## 历史批次对比

分析方案将 `product_series` 配置为同类比较键，候选周期必须具备并匹配该键；产品代码、配方版本、模芯/模架和检测结论作为比较维度。曲线按“阶段 + 阶段内相对时间”对齐，而不只按周期开始后的秒数对齐。

每个周期应完整参与计算：600 组 × 13 值。接口响应可以分页或返回有界统计摘要，但不能用单页 500 条代替全周期计算。

## 投产前必须替换的样例假设

- `recipe_step` 的 10、20、30、40、50；
- 5 个阶段的边界和名称；
- 所有示例配方值、设备地址和产品标识；
- 字段单位、压力基准和换算；
- 原图对象存储的保留期限、不可变策略、备份和访问审计要求。
