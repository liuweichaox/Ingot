# 代码结构规划

> 配套 [roadmap.md](roadmap.md)。本文定义代码边界、依赖方向与迁移纪律，不定义业务功能。

---

## 1. 总体设计

Ingot 采用 **模块化单体 + Clean Architecture 依赖规则 + 业务能力垂直切片**。

- 一级目录表达运行与所有权边界：`shared`、`edge`、`platform`、`agent`。
- 项目表达需要编译期隔离的能力边界，不按每个实体或用例拆项目。
- 项目内部按业务能力组织，例如 `Events`、`Inspections`、`Cycles`、`Analysis`。
- Edge 与 Platform 是独立部署单元，只通过 `Contracts` 通信。

中心侧统一使用 **Platform** 命名。`Central` 只描述相对于 Edge 的部署位置，无法稳定表达主数据、分析、集成和 Agent 等完整平台能力，也不适合未来多工厂、多区域部署。

---

## 2. 当前结构

第一批纯目录与命名迁移已经完成：

```text
src/
├── shared/
│   ├── Ingot.Domain/                     共享业务内核
│   └── Ingot.Contracts/                  HTTP 与跨进程契约
│
├── edge/
│   ├── Ingot.Edge.Application/           Edge 抽象与配置
│   ├── Ingot.Edge.Infrastructure/        SQLite、日志、指标、HTTP 上行
│   └── Ingot.Edge.ConnectorHost/         现场入口宿主
│
├── platform/
│   ├── Ingot.Platform.Infrastructure/    第二批拆分前的过渡容器
│   ├── Ingot.Platform.Api/               HTTP 宿主、鉴权、健康检查
│   └── Ingot.Platform.Web/               工程师工作界面
│
└── agent/
    ├── Ingot.Agent/                      有界运行时、护栏、结果与原始记录核对
    └── Ingot.Agent.Providers/            模型客户端与运行存储

apps/
├── website/
└── docs-site/
```

`Ingot.Platform.Infrastructure` 是明确的过渡项目，不是长期目标。它仍然包含生产记录存储、主数据、周期计算、分析工具和 Webhook，后续按路线图阶段 2 拆分。

---

## 3. Platform 目标结构

```text
src/platform/
├── Ingot.Platform.MasterData/            Phase、Mapping、Feature、Inspection 定义
├── Ingot.Platform.Analytics/             周期物化、聚合下推、特征计算、幂等重算
├── Ingot.Platform.Persistence/           事件、检测、相关记录的生产记录存储
├── Ingot.Platform.Integration/           Webhook、SSE 投递、Edge 注册
├── Ingot.Platform.Tools/                 IAnalysisTool 薄层包装
├── Ingot.Platform.Api/
│   ├── Features/
│   │   ├── Events/
│   │   ├── Inspections/
│   │   ├── Cycles/
│   │   ├── Agents/
│   │   ├── Edges/
│   │   └── Subscriptions/
│   ├── Authentication/
│   ├── Configuration/
│   └── HealthChecks/
└── Ingot.Platform.Web/
    └── src/
        ├── app/
        ├── features/
        └── shared/
```

### MasterData

保存阶段定义、步序映射、特征定义和检测定义。主数据低频变化、需要版本和历史回填，与高频不可变记录具有不同生命周期。

### Analytics

承载 `cycle_phases`、`cycle_features`、SQL 聚合下推和迟到事件重算。它可以依赖数据库计算能力，但不得依赖 HTTP、Agent 或 API 宿主。

### Persistence

保存不可变生产事件、检测记录和相关记录 metadata。接口与实现按记录类型分目录，不承载跨周期统计逻辑。

### Integration

承载 Webhook、CloudEvents、SSE 投递和 Edge 注册。对外通信失败不得污染记录与计算模块。

### Tools

把 Platform 的确定性查询和计算包装为 `IAnalysisTool`。只定义输入 schema、调用能力、组装 `Data`、`Details`、`RelatedRecords` 与 `Limitations`；不得包含 SQL 或统计计算。

---

## 4. 依赖规则

```text
Domain                      → 无依赖
Contracts                   → Domain

Edge.Application            → Domain
Edge.Infrastructure         → Edge.Application, Contracts
Edge.ConnectorHost          → Edge.Application, Edge.Infrastructure, Contracts

Agent                       → Contracts
Agent.Providers             → Agent, Contracts

Platform.MasterData         → Domain, Contracts
Platform.Analytics          → Domain, Platform.MasterData
Platform.Persistence        → Domain, Contracts
Platform.Integration        → Domain, Contracts
Platform.Tools              → Agent, Platform.Analytics, Platform.Persistence 的读取接口
Platform.Api                → Platform 各模块，仅负责组合与 HTTP 适配
```

必须持续满足：

1. `Edge.*` 不引用 `Platform.*`。
2. `Agent` 与 `Agent.Providers` 不引用 `Platform.*`。
3. `Platform.Analytics` 不引用 `Agent` 或 `Platform.Api`。
4. `Platform.Infrastructure` 及未来拆出的模块不引用 `Platform.Api`。
5. `Platform.Tools` 不直接引用 Postgres 实现，不包含实质计算。

---

## 5. 领域模型策略

当前 `Ingot.Domain` 是 Edge 与 Platform 共享的最小内核，主要包含 `ProductionEvent` 与通用值对象。

不预先创建空的 `Ingot.Platform.Domain`。当 `Cycle`、`Phase`、`FeatureDefinition` 或 `InspectionRecord` 的规则在 DTO 校验、SQL 和工具中出现重复时，再把对应不变式提取到 `Ingot.Platform.Domain`。这是按需求发生的领域演进，不作为纯目录重构执行。

同理，暂不引入泛化的 `Ingot.Platform.Application`。`MasterData` 与 `Analytics` 已经表达当前最重要的应用能力；只有同一用例需要被多个入口复用且编排开始重复时，才增加用例层。

---

## 6. 迁移顺序

### 批次 1：边界和命名，已完成

- 建立 `shared`、`edge`、`platform`、`agent`、`apps` 分组。
- Edge 项目统一为 `Ingot.Edge.*`。
- 中心侧统一为 `Ingot.Platform.*`。
- `Ingot.Agent.Infrastructure` 更名为 `Ingot.Agent.Providers`。
- Docker、Compose、脚本、解决方案和文档同步使用新路径。
- 新配置使用 `PlatformApiBaseUrl`，旧 `CentralApiBaseUrl` 保留兼容入口。

### 批次 2：拆 Platform.Infrastructure

结合 roadmap 阶段 2 执行：

- `Cycles` 与新增聚合代码进入 `Platform.Analytics`。
- 检测定义、阶段定义和特征定义进入 `Platform.MasterData`。
- 事件、检测和相关记录存储进入 `Platform.Persistence`。
- Webhook 与 Edge 注册进入 `Platform.Integration`。
- `AgentTools` 变成 `Platform.Tools` 薄层，计算下沉到 Analytics。

### 批次 3：领域与测试随功能演进

- 出现重复不变式时新增 `Platform.Domain`。
- API Controller 按 `Features` 重组，不改变公开路由。
- 边界稳定后增加架构测试。

---

## 7. 测试目标

```text
tests/
├── Ingot.Domain.Tests/
├── Ingot.Contracts.Tests/
├── Ingot.Agent.Tests/
├── Ingot.Platform.Analytics.Tests/       SQL 聚合与逐行参考实现交叉验证
├── Ingot.Platform.Api.Tests/             HTTP 与端到端契约
├── Ingot.Edge.Tests/
├── Ingot.IntegrationTests/
└── Ingot.Architecture.Tests/
```

在批次 2 之前保留现有 `Ingot.Core.Tests`，避免目录迁移和测试拆分同时制造无价值冲突。

---

## 8. 明确不做

| 不做 | 原因 |
|---|---|
| 为每个聚合根建立项目 | 当前规模不足以支撑这种拆分成本 |
| 把 Contracts 拆成多个项目 | 契约集中更容易审查破坏性变化 |
| 立即建立空 Platform.Domain/Application | 空层不能提供真实边界，只会增加引用 |
| 在批次 1 同时拆 Platform.Infrastructure | 必须结合阶段 2 的真实计算代码确定边界 |
| 让 Platform.Tools 承担统计计算 | 数字必须来自可测试、可复现的 Analytics |
