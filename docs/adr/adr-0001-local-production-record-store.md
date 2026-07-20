# ADR-0001：中心数据存储的本地数据库选型与架构优化

**状态（Status）：** Proposed（v2，已放宽约束）
**日期（Date）：** 2026-07-19
**决策者（Deciders）：** Ingot 维护者（@liuweichaox）
**约束（硬性）：** 数据库必须**本地自托管**（不依赖云/托管服务；**允许以 Docker 容器方式在本机部署**，无需是进程内嵌入式）；数据库**类型不限**。

> v2 变更：约束由"必须嵌入式"放宽为"本地自托管、可 Docker"。这重新打开了"为时序/事件负载专门优化的服务型数据库"这条路，最优解随之改变（见 §2）。

---

## 1. 背景（Context）

### 1.1 当前架构

```text
设备/仪器/MES/ERP ──适配──> ProductionEvent[] / InspectionRecord
        ▼
  Platform API (.NET 10)  ──────────────────────>  PostgreSQL（Docker，本地）
        ├─ 鉴权 / 契约校验 / 幂等                          ├─ production_events（手写按月 RANGE 分区, JSONB+GIN）
        ├─ 查询 + SSE（轮询 ingest_id 游标）               ├─ event_ingest_keys（幂等键）
        ├─ Ingot Chat（只读数据工具 + 有界调查）           ├─ inspection_records
        └─ Webhook 投递                                   └─ webhook_subscriptions
        └─ SQLite ×4：Chat run / 边缘 outbox / 日志 / 边缘注册
```

现状（代码走查）：中心数据存储已经是 **Docker 本地部署的 PostgreSQL**；事件表按 `occurred_at` **手写**月度 RANGE 分区、`context` 用 JSONB+GIN、幂等靠 `event_id` 主键 + `(edge_id,seq)` 唯一键的"先抢占后插入"两表事务。

### 1.2 关键洞察：现状离最优只差一个扩展

约束放宽后，真正的问题不再是"要不要放弃 Postgres"，而是**"在可 Docker 本地部署的前提下，什么数据库最贴合 Ingot 的负载画像"**。而 Ingot 的负载画像是典型的**时序 / 事件流**：

- **写**：批量（≤500/批）、**同步幂等**（每批需当场返回 accepted/duplicate）、逐批事务、多边缘并发追加、`occurred_at` 天然时间轴。
- **读（范围/游标）**：按时间、subject、correlationId、context(JSON) 过滤，`ingest_id` 单调游标驱动 SSE。
- **读（分析/Chat）**：`check_data_quality`（完整性、**新鲜度**、序号缺口）、`get_cycle_trace`（关联 + subject 时间窗）——全是**面向时间窗的聚合**。
- **保留**：按月维护 / 丢弃。

这套画像里，"手写月度分区 + 手写新鲜度/完整性聚合"正是 **TimescaleDB 用 hypertable + 连续聚合（continuous aggregate）自动化掉的东西**。也就是说，现状是"用裸 Postgres 手搓了一个半成品时序库"，而最优解是把这些手搓件换成专用扩展。

### 1.3 放弃/保留什么（诚实评估）

- **不需要放弃**：Npgsql 驱动、SQL 方言、JSONB、幂等两表事务、现有契约与校验——TimescaleDB 是 Postgres 扩展，全部平移。
- **需要评估的新变量**：若选 ClickHouse 这类列式 OLAP，能换来极致分析/压缩，但会**牺牲同步幂等**（其去重是异步 merge），不适合做系统源。

---

## 2. 决策（Decision）

**推荐：中心数据存储改用 TimescaleDB（Docker 本地部署）作为默认档；保留嵌入式 SQLite 作为"边缘/气隙单机"可选档；两者藏在同一个 `IPlatformEventStore` 抽象后，按部署配置切换。**

- **默认档 — TimescaleDB（Docker）**：把 `production_events` 建为 **hypertable**（按 `occurred_at` 自动分块，取代手写分区 DDL）；为 data-quality/freshness 建 **连续聚合**；对冷块启用**原生列式压缩**；用**保留策略**自动丢弃过期块。幂等两表事务、JSONB、SSE 游标全部保留。
- **可选档 — 嵌入式 SQLite SoR**：面向真正离线/单机/气隙现场（无 Docker 亦可跑），按月库文件 + 单写队列 + WAL（详见附录 A）。
- **规模逃生舱 — ClickHouse（下游分析镜像，非系统源）**：仅当事件体量/分析压力爆发时，从 Timescale 经 CDC 同步到 ClickHouse 只读分析，**绝不**用它承接幂等写。

一句话：**用 TimescaleDB 把"手搓时序库"升级成"专用时序库"，迁移近零成本、保住全部可信保证、并直接改善 Chat 分析**。

---

## 3. 备选方案（Options Considered）

### Option A：裸 PostgreSQL（维持现状）
| 维度 | 评估 |
|---|---|
| 复杂度 | 低（零改动） |
| 迁移成本 | 无 |
| 时序/分析契合度 | 中（分区/聚合全靠手写） |
| 团队熟悉度 | 高 |

**Pros：** 零风险基线；JSONB/事务/幂等已跑通。
**Cons：** 月度分区、新鲜度/完整性聚合、压缩、保留全靠手写维护——正是本 ADR 想消除的负担。

### Option B：TimescaleDB（Docker）【推荐 · 默认档】
| 维度 | 评估 |
|---|---|
| 复杂度 | 低（Postgres 扩展，SQL/驱动不变） |
| 迁移成本 | **极低**（`CREATE EXTENSION` + 建 hypertable + 数据搬迁） |
| 时序/分析契合度 | **高**（hypertable 自动分块、连续聚合、压缩、保留策略） |
| 团队熟悉度 | 高（就是 Postgres） |

**Pros：** 自动时间分块替代手写分区 DDL（本会话刚加固的 `EnsurePartitionAsync` 复杂度直接消失）；**连续聚合**天然服务 `check_data_quality` 的新鲜度/完整性与 `get_cycle_trace`；冷块列式压缩降存储；**保留策略**自动化丢弃；幂等/JSONB/SSE 全保留；官方 Docker 镜像本地部署。
**Cons：** 多一个扩展依赖（镜像 `timescale/timescaledb`）；连续聚合/压缩策略需要一次性设计调优；仍是服务型（但约束已允许 Docker）。

### Option C：ClickHouse（Docker）
| 维度 | 评估 |
|---|---|
| 复杂度 | 中高（新引擎、新心智） |
| 迁移成本 | 高（SQL 方言、驱动、数据建模都变） |
| 时序/分析契合度 | 分析**极高**，OLTP 幂等**低** |
| 团队熟悉度 | 低 |

**Pros：** 列式，摄入与大时间窗聚合极快、压缩极好，适合超大事件量。
**Cons：** **去重是异步 merge（ReplacingMergeTree）**，无法当场返回 accepted/duplicate → **不适合做可信系统源**；无强事务；JSON/关系含义弱于 JSONB。定位应为"下游只读分析镜像"，非 SoR。

### Option D：嵌入式 SQLite SoR（+ 可选 DuckDB 分析）【可选 · 边缘档】
| 维度 | 评估 |
|---|---|
| 复杂度 | 中（写队列 + 月库文件管理自建） |
| 迁移成本 | 中 |
| 时序/分析契合度 | 写好、分析弱（DuckDB 补） |
| 团队熟悉度 | 高（已 4 处在用） |

**Pros：** **零服务、单文件备份、无 Docker 也能跑**——离线/气隙/单机边缘的最佳形态；与现有 4 处 SQLite 统一引擎。
**Cons：** 单写者并发天花板（写队列缓解）；重分析弱（需挂 DuckDB）；跨月 `ATTACH`。**在允许 Docker 后，它从"主推"降级为"边缘特化档"。** （完整设计见附录 A。）

### 快速否决
- **QuestDB / InfluxDB**：时序快，但**同步幂等 / 关系 + JSON 过滤 / 相关记录式关系模型**支持弱，与"可信记录 + 幂等 + 原始记录链接"定位不合。
- **MongoDB**：文档/JSON 原生，但面向时间窗聚合与数值相关记录模型不如 JSONB/Timescale，且相对现状是纯增复杂度。

---

## 4. 权衡分析（Trade-off Analysis）

- **同步幂等（可信底线）**：Postgres/Timescale/SQLite = 原生唯一约束 + 事务，**当场**判重；ClickHouse = 异步去重，**出局做 SoR**。这是把 ClickHouse 限定为下游镜像的决定性理由。
- **时序自动化**：Timescale（hypertable + 连续聚合 + 压缩 + 保留）> 裸 PG（全手写）> SQLite（月库文件手写）。这正是 Timescale 对 Ingot 的核心增量。
- **分析延迟**：ClickHouse > Timescale（连续聚合预算物化）> 裸 PG > SQLite。Timescale 的连续聚合让"新鲜度/完整性"这类高频 Chat 聚合走预物化，接近 OLAP 体验而无需第二引擎。
- **迁移风险**：Timescale ≈ 0（同驱动同 SQL）；ClickHouse / SQLite = 中高。**这是 Timescale 压过 ClickHouse 的实务关键**——同样是"更适合时序"，Timescale 几乎白拿。
- **JSON context**：JSONB+GIN（PG/Timescale）最强；ClickHouse/SQLite 次之。
- **运维/footprint**：SQLite（无服务）< Timescale/PG（一个容器）< ClickHouse（更重）。约束允许 Docker 后，一个 Timescale 容器的运维成本可接受。
- **可扩展性**：ClickHouse/Timescale 具备扩展/压缩以吃大体量；SQLite 单机封顶（边缘场景无所谓）。

**结论排序（作为系统源）：** Timescale > 裸 PG > SQLite ≫ ClickHouse（ClickHouse 仅作下游分析）。

---

## 5. 后果（Consequences）

**变得更容易：**
- 删除手写分区维护：`EnsurePartitionAsync` / 月表 DDL 由 hypertable 自动分块取代（本会话对它做的"移出事务 + 缓存"加固可退役，复杂度归零）。
- Chat 分析：新鲜度/完整性/趋势走连续聚合，查询更快更稳。
- 存储与保留：冷块压缩 + 保留策略自动化，省手工清理。
- 迁移轻：仍是 Postgres，代码面几乎只改建表/建策略的 DDL。

**变得更难 / 新增能力：**
- 需一次性设计 **hypertable 分块间隔、连续聚合刷新策略、压缩阈值、保留窗口**（配置化）。
- 运行时多一个 `timescale/timescaledb` 镜像（替换现 `postgres` 镜像即可）。
- 若要边缘档，需实现并维护 SQLite SoR（附录 A）——两档共用 `IPlatformEventStore`。

**Revisit triggers：**
- 事件体量/分析并发爆发 → 引入 ClickHouse 下游镜像（另开 ADR）。
- 出现多节点/HA 硬需求 → 评估 Timescale 多节点或外部时序集群。

---

## 6. 数据模型映射（PostgreSQL → TimescaleDB）

| 现状（裸 PostgreSQL） | 目标（TimescaleDB） |
|---|---|
| `production_events` 手写月度 RANGE 分区 + 按需 `CREATE PARTITION` | `SELECT create_hypertable('production_events','occurred_at', chunk_time_interval => INTERVAL '1 month')` —— **自动分块**，删掉 `EnsurePartitionAsync` |
| `event_ingest_keys`（幂等两表事务） | **原样保留**（Timescale 支持唯一约束 + 事务） |
| `ingest_id` 单调序列（SSE 游标） | **原样保留**（普通序列/BIGINT，供 `ingest_id > cursor`） |
| `context JSONB` + GIN | **原样保留**（JSONB + GIN） |
| Chat：`check_data_quality` 新鲜度/完整性即时聚合 | **连续聚合**（`CREATE MATERIALIZED VIEW ... WITH (timescaledb.continuous)`）按 subject/时间桶预物化 last-seen / 计数 / 缺口 |
| `get_cycle_trace` 关联+subject 时间窗 | 走 hypertable 时间维索引 + 连续聚合，原始记录链接契约不变 |
| `DROP PARTITION` 保留 | `add_retention_policy('production_events', INTERVAL '18 months')` |
| 冷数据存储 | `add_compression_policy(...)` 列式压缩老块 |
| `inspection_records` / `webhook_subscriptions` | 保持普通表（非时序，不必 hypertable） |

**本会话加固的继承：** 幂等 reserve+insert 事务、`HasSequenceGap`、以及新加的 `PlatformIngestWindow` **全部保留并仍有价值**——时间窗校验此时用于约束 hypertable 不被异常时间戳撑出无意义的远期块。

---

## 7. 行动项（Action Items）

**默认档 —— 迁移到 TimescaleDB**
1. [ ] `docker-compose.app.yml`：`postgres:xx` 镜像换为 `timescale/timescaledb:xx-pgYY`；连接串/凭据不变。
2. [ ] `PostgresPlatformEventStore.InitializeAsync`：建表后 `CREATE EXTENSION IF NOT EXISTS timescaledb;` + `create_hypertable(...)`；**移除**手写分区逻辑（`EnsurePartitionAsync` 及月表 DDL）。
3. [ ] 为 `check_data_quality` 的新鲜度/完整性建**连续聚合**视图 + 刷新策略；改造 `ChatEventReader`/data-quality 工具优先查聚合，回退明细。
4. [ ] 配置化 `chunk_time_interval` / 压缩阈值 / 保留窗口（沿用 `PlatformEventOptions`，与 `PlatformIngestWindow` 呼应）。
5. [ ] 数据迁移：现有 `production_events` → hypertable（`INSERT ... SELECT` 或 `create_hypertable(..., migrate_data => true)`）。
6. [ ] 压测：用 `scripts/benchmark-platform-ingest.sh` 对比 裸 PG vs Timescale 的摄入 P50/P99 与大时间窗聚合延迟，确认无回退、聚合有提升。
7. [ ] 文档：更新 `architecture.md` / 部署文档，说明 Timescale 默认档与调参。

**可选档 —— 边缘 SQLite SoR（按需）**
8. [ ] 在 `IPlatformEventStore` 后增 `SqlitePlatformEventStore`（配置切换），实现月库文件生命周期 + 单写队列 + WAL（附录 A）。

**逃生舱 —— ClickHouse（远期）**
9. [ ] 仅当分析压力爆发：从 Timescale CDC/批同步到 ClickHouse 只读镜像，Chat 重聚合切过去，SoR 仍在 Timescale。

**决策校验**
10. [ ] 确认默认档选 **Timescale**；若你更想零改动先稳住，可先停在 Option A，仅补连续聚合思路。

---

## 附录 A：嵌入式 SQLite SoR 设计（边缘/气隙档，保留自 v1）

按月独立库文件 `events_YYYYMM.db`（写当前月、查按需 `ATTACH`、保留=删文件 O(1)）；`event_ingest_keys` 同构表与插入**同事务**保幂等；`INTEGER PRIMARY KEY AUTOINCREMENT` 提供单调 `ingest_id`；`context` 高频键建 `json_extract` 表达式索引；**单写连接 + Channel 写队列**串行化、`WAL + synchronous=NORMAL`；重分析可挂 DuckDB（`sqlite_scanner` attach 或读冷月 Parquet）。适用于无 Docker 的单机离线现场。

---

## 8. 一句话总结

约束放宽到"可 Docker 本地部署"后，最优解不是换范式，而是**把手搓的时序能力升级为专用时序库**：**TimescaleDB 作默认档**（迁移近零、保住幂等与 JSONB、自动分块 + 连续聚合直接改善 Chat），**嵌入式 SQLite 作边缘/气隙档**，**ClickHouse 仅作远期下游分析镜像**。
