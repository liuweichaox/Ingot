# 宏观架构

Ingot 由 Platform API、Platform Web、数据存储和使用方实现的数据源适配组成。Ingot 是制造数据采集与工艺分析平台；Platform Web 中的 Ingot Chat 是工程师的主要入口，提供快速查询和可选的有界多 Agent 深入分析。数据源通过标准事件 API 接入。

```text
设备、仪器、业务系统或自定义数据源
  → 使用方适配与运行
  → POST /api/v1/events:batch
  → Platform API
 → TimescaleDB（PostgreSQL + 时序扩展）生产数据
  → 查询、SSE 与 Platform Web · Ingot Chat
```

如需现场本地持久化和 outbox，使用方可部署 `Ingot.Edge.ConnectorHost` 作为可选入口：适配程序提交 `ProductionEvent[]` 到 Host，Host 再向 Platform 批量上报。该运行方式由使用方拥有和运维。

## 产品边界

- Ingot Chat 运行在 Platform Web，只查询数据、检查数据质量并返回相关记录；综合分析模式由工艺、质量和复核角色审查同一批已查到的生产记录，不能访问设备或自行扩展数据范围。
- 使用方负责设备协议、字段映射、凭据、离线缓存、重试和本地进程监管。
- `Ingot.Edge.ConnectorHost` 是可选的用户自管本地事件入口与 outbox；数据源适配的实现与运行由使用方负责。
- `POST /api/v1/events:batch` 接受经过令牌和契约校验的标准事件批次。
- `GET /api/v1/inspection-tasks` 从已完成周期派生待检任务；`POST /api/v1/inspection-records` 只接受关联待检周期的检测记录。
- `POST /api/v1/inspection-plans` 保存和发布版本化质量方案；方案按产品、配方、设备和生效时间决定待检项目。Platform 不内置行业必检代码。
- 视觉检查原图由 `POST /api/v1/inspection-attachments` 写入 `Data/inspection-attachments` 持久卷，服务端计算 SHA-256；`GET /api/v1/inspection-attachments/{attachmentId}/content` 提供原始文件复核。原图不跟随生产事件保留策略删除。
- `POST /api/v1/inspection-reviews` 追加视觉复核结论，`GET /api/v1/inspection-reviews/audit` 查询原图访问和复核审计；这些接口使用统一 Platform 身份角色。
- `GET /api/v1/cycle-comparisons/{correlationId}` 对同产品系列历史周期执行全采样确定性比较。
- `GET /api/v1/cycles` 提供周期级生产工作台：聚合生产状态、完整采样、配置阶段和质量方案执行状态；高频样本会通过摄入游标完整读取，不以单页传输大小作为业务截断。
- Platform Web 将“生产周期”作为日常工作入口，将“数据质量”和“历史对比”归入分析治理；原始生产事件保留在“事件查询”中供诊断和追溯。检验录入从待检任务打开抽屉，不长期占据工作台左侧。
- “事件查询”使用 `beforeIngestId` 游标按需加载更早记录，实时订阅从当前最新序号继续；页面不会一次渲染全部历史事件，但完整周期查询和分析仍通过内部游标读取全部数据。
- 生产记录查询、Chat 和 SSE 均通过 Platform API 提供。
- 实时控制、安全联锁和设备写操作不属于 Ingot。

## 存储与网络

- TimescaleDB（PostgreSQL + 时序扩展）：中心生产事件、检测记录和查询数据。生产事件表为 hypertable，按 `occurred_at` 自动分块，并可按配置启用块级压缩与保留策略；幂等去重仍由独立的 `event_ingest_keys` 键表承担。本地自托管（Docker 镜像 `timescale/timescaledb`），无需外部托管服务。
- 受控附件目录：视觉检查原图按内容哈希分目录长期保存，元数据写入 PostgreSQL，文件正文落在 Docker 的 `Data` 持久卷。缩略图和标注图只能作为派生文件，不能替代原图。
- SQLite：部署可选的本地缓存、日志或边缘运行状态；亦作为离线/气隙单机的可选生产记录库形态。其运行方式由使用方选择。
- Chat 只经 Platform 数据查询服务读取数据；模型配置不会改变数据权限或工具白名单。
- 生产环境应将 Platform 与数据库置于受控网络边界，并使用 TLS、令牌轮换和最小化数据访问范围。

参见 [Ingot Chat](chat.md)、[生产事件规范](rfc-production-events.md)和[部署](tutorial-deployment.md)。
