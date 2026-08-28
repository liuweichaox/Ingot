# 部署运维

> 文档状态：**当前运维指南**。部署目标是让数据采集、业务记录和工程决策在厂内长期可靠运行；公开官网和文档站不属于工厂运行时。

本文定义长期运行环境的服务部署、配置管理、可观测性、故障恢复、备份和升级要求。本地开发与验证步骤见[快速开始](getting-started.md)。

本文描述仓库当前可运行形态。多副本、PITR、站点生产单元和受控行动的目标要求见[生产架构](production-architecture.md)；未通过其中准入门槛的部署不得宣称已经达到对应生产等级。

## 推荐拓扑

```text
现场数据源                              工厂运行环境
控制系统 / 仪器 / 视觉 / 检验 / MES
          └─ Edge ConnectorHost ───→ Platform API
                                      ├─ Platform Worker（持久任务执行）
                                      ├─ PostgreSQL / TimescaleDB
                                      ├─ 附件与工艺知识文件
                                      ├─ Optimizer（独立无状态服务）
                                      ├─ Agent / Chat（Platform 内嵌业务能力）
                                      └─ Platform Web（独立 React 前端）
```

Platform API 负责请求、Chat 消息事务和业务事务，Platform Worker 负责持久 Chat 运行、知识提取、分析回填、实验固化和保留任务。两者共享 PostgreSQL 任务租约，但不共享进程内队列。`Edge.Application`、`Edge.Infrastructure`、`Platform.Infrastructure` 和 Agent 是代码层类库，不是独立 Compose 服务。

仓库自带的 Compose 是单 API 实例参考拓扑，不宣称高可用。Agent 运行已经与业务证据一起进入 PostgreSQL，因此不再阻止外部编排器在共享数据库和负载均衡器之后扩展 API；正式多副本部署仍需自行提供入口负载均衡、数据库高可用和容量验收。

### Edge 划分原则

每个 Edge ConnectorHost 拥有明确的 `SiteId` 归属，以及独立 `EdgeId`、进程或容器、数据卷、配置缓存和生命周期。`SiteId` 是生产单元边界；Platform 会把 Edge token、`EdgeId` 与 `SiteId` 绑定，持有正确 token 的 Edge 也不能向另一个站点写入数据。

生产读路径同样按 `SiteId` 失败关闭。OIDC 颁发者必须为非管理员身份签发一个或多个 `ingot:site` 声明；本地账户由平台管理员在用户管理中分配站点。`platform.admin` 可进行跨站点管理查询，但按运行号读取详情、分析和曲线仍要求显式给出 `siteId`，避免把同名运行误归到其他生产单元。

- 同一 OT 网络和允许共同中断的设备可以共用一个 Edge；
- 跨 VLAN、安全区或物理隔离网络分别部署 Edge；
- 关键设备、事件率高或本地积压大的设备使用独立 Edge；
- 按电源、主机、交换机、链路和维护窗口判断共同故障域；
- 以现场可接受的停采范围和恢复时间决定实例数量。

小型现场可以让 Edge 与 Platform 共用物理服务器，但仍保持独立进程、存储、健康检查、升级和恢复。

官网与文档站使用 `deploy/compose.yml` 单独部署，不进入工厂应用故障域。

## 环境配置

```bash
cp .env.example .env
```

至少修改：

- `INGOT_POSTGRES_PASSWORD`
- `INGOT_SITE_ID`：当前部署所属生产单元；投产后不得随意修改
- `INGOT_EDGE_ID`：安装后保持不变，每台 Edge 唯一
- `INGOT_EDGE_TOKEN`
- `INGOT_CONNECTOR_TOKEN`
- `INGOT_CONNECTOR_LOCAL_TOKEN`
- `INGOT_EDGE_DIAGNOSTICS_BASE_URL`：Platform 固定访问该 Edge 诊断 API 的可信地址；不得使用节点上报值动态改写
- `INGOT_ADMIN_PASSWORD`

生产环境必须使用 `INGOT_AUTH_MODE=Local` 或 `INGOT_AUTH_MODE=Oidc`。`INGOT_AUTH_MODE=Disabled`
只允许在明确隔离的演示环境使用，并且必须同时设置 `INGOT_ALLOW_INSECURE_DEMO=true`；该模式会把
所有请求映射到固定的开发身份，不能暴露到厂内网络或反向代理之后。

### OIDC 身份提供方

OIDC 模式使用 Authorization Code + PKCE，前端是不持有客户端密钥的公共客户端。至少配置：

- `INGOT_AUTHORITY`：OIDC issuer/authority 的 HTTPS URL；
- `INGOT_AUDIENCE`：Platform API 期望的 access-token audience；
- `INGOT_OIDC_CLIENT_ID`：在身份提供方注册的 SPA public client ID；
- `INGOT_OIDC_SCOPE`：必须包含 `openid`，并加入身份提供方为 Platform API 配置的 scope；
- `INGOT_OIDC_NAME_CLAIM_TYPE` 与 `INGOT_OIDC_ROLE_CLAIM_TYPE`：分别默认为 `name` 和 `roles`；
- `INGOT_OIDC_ALLOWED_ORIGINS`：用空格分隔 discovery、token endpoint 和静默续期所需的 HTTPS origin；必须包含 authority origin，不允许通配符或路径。

以 Platform Web 的对外 origin 为 `https://platform.example.com` 时，身份提供方必须精确登记以下 URI：

```text
https://platform.example.com/auth/callback
https://platform.example.com/auth/silent-callback
https://platform.example.com/auth/logout-callback
```

不得使用通配回调 URI。身份提供方还必须允许该 SPA 从浏览器访问 token endpoint。access token 的角色 claim 必须包含一个平台岗位，非管理员身份还必须包含一个或多个 `ingot:site`。

不要提交 `.env` 或真实设备凭据。设备密码和证书应使用现场允许的密钥管理方式注入。

### 模型服务

启用 Chat 时，模型服务应提供 OpenAI-compatible 接口。平台管理员在“系统管理 → 模型服务”页面配置供应商标签、`Responses` 或 `ChatCompletions` 协议、API 根地址、模型标识和 API key；更换兼容模型服务只修改页面配置，不修改 Ingot 源码。API key 为只写字段，经服务端加密后存入数据库，浏览器和读取接口只能看到是否已配置及末四位提示。DeepSeek 的一个配置示例是 `Provider=DeepSeek`、`Protocol=Responses`、`BaseUrl=https://api.deepseek.com`，并使用当前可用的 DeepSeek 模型标识。Platform 启动时只探查模型清单；只有服务实际执行 Chat 时才会把相关问题、页面上下文和只读工具结果发送给所选模型服务。启用外部服务前必须确认这些材料可以发送到该服务所在区域。生产部署必须持久化并保护 `DataProtection:KeysPath`，否则数据库中的 API key 无法在容器重建后解密。

机理知识语义草稿默认关闭。需要时显式设置 `INGOT_MECHANISM_DRAFT_ENABLED=true`、`INGOT_MECHANISM_DRAFT_BASE_URL`、`INGOT_MECHANISM_DRAFT_MODEL` 和独立的 `INGOT_MECHANISM_DRAFT_API_KEY`；启用前必须确认知识片段可发送到该服务所在区域。该能力只返回可编辑草稿，不自动持久化、审核或激活声明。

模型服务不是采集、检验或数值优化的启动依赖。发送给模型的内容必须经过授权工具和业务权限控制。

## 启动与停止

先校验必填环境变量和 Compose 结构：

```bash
docker compose -f docker-compose.app.yml config --quiet
```

构建并启动五个核心服务：

```bash
docker compose -f docker-compose.app.yml up -d --build
```

常用生命周期命令：

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
docker compose -f docker-compose.app.yml restart platform-api
docker compose -f docker-compose.app.yml down
```

`down` 停止并移除容器和网络，但默认保留命名数据卷；不要在没有备份和明确重置意图时添加 `--volumes`。修改源代码后使用 `up -d --build`；只修改 `.env` 时使用 `up -d` 重新创建受影响容器。

首次构建会下载较大的 SDK、PyTorch 和数据库镜像。必须等 `platform-migrate` 成功退出，且 PostgreSQL、Platform API、Platform Worker、Optimizer、Web 和 ConnectorHost 均为 `healthy` 后，才算启动完成。

| 现象 | 先检查 | 常见原因与处理 |
|---|---|---|
| `ps -a` 没有任何容器 | 构建命令的最后输出 | 构建仍在进行或已中断；重新执行 `up -d --build` |
| `unexpected EOF` 或 `short read` | 镜像下载层 | 网络中断导致层不完整；直接重试，Docker 会复用完整层 |
| Web 未启动、API 已健康 | `logs platform-web` | 前端构建或 Nginx 配置失败 |
| API 反复重启 | `logs platform-migrate`、`logs platform-api` 和 `logs postgres` | 数据库密码、迁移、目录权限或生产配置校验失败 |
| Optimizer 不健康 | `logs optimizer` 和 `/ready` | Python 数值依赖尚未安装完成或运行时加载失败 |
| 登录口令未知 | `logs platform-migrate` | Migrator 仅在空用户表首次引导且密码留空时输出随机口令；已有账户不会因修改 `.env` 自动重置 |
| 端口已占用 | `lsof -nP -iTCP:3000 -iTCP:8000 -iTCP:8100 -sTCP:LISTEN` | 停止占用进程，或有计划地修改 Compose 端口映射 |

查看单个服务日志时使用 `docker compose -f docker-compose.app.yml logs --tail=200 <服务名>`。排障时保留完整错误信息，不要先删除容器、镜像或数据卷。

### Edge 到 Platform 的传输安全

Edge 通常运行在独立车间主机上。生产部署必须在 Edge 与 Platform API 之间提供 TLS 终结，
例如使用 nginx 或 Caddy 配置内部 CA 证书，并让 Edge 的 `Edge:PlatformApiBaseUrl`
（环境变量形式为 `Edge__PlatformApiBaseUrl`）使用 `https://`。不要在跨主机或不可信厂内网段上以 HTTP 传输 Bearer token。

Platform、Optimizer 和数据库不应直接暴露给非必要网段；反向代理只公开必要的 Web/API 入口，
并单独保护 `/metrics`。

需要独立现场连接器时：

```bash
docker compose -f docker-compose.app.yml --profile connector-host up -d --build
```

连接真实设备前，先在 Platform 中配置目标 Edge、连接信息和数据映射。示例地址、密码和工艺范围不能直接用于生产。

## 采集配置运行规则

Platform 按 `EdgeId` 发布版本化采集配置。Edge 主动拉取、在本地验证，并把最后成功版本保存到 `Data/acquisition-deployments.json`。

- Platform 暂时不可用时，Edge 继续使用最后成功版本；
- 新版本连接、点位或换算验证失败时，不替换旧版本；
- Platform 明确发布零配置时，Edge 停止对应采集并报告状态；
- 生产环境禁止静默启用未版本化本地 fallback；
- HTTP/MQTT 可以读取示例报文，OPC UA 可以浏览节点；
- Modbus TCP 和 MELSEC 只读取用户明确配置的地址，不进行盲扫；
- HTTP、MQTT、OPC UA、Modbus TCP 和 MELSEC 默认拒绝所有未登记目标；设备主机必须显式加入 `Acquisition:Security:AllowedHttpHosts`（HTTP）或 `AllowedNetworkHosts`，并固定使用校验过的 DNS 解析结果；回环、链路本地、未指定和组播地址即使列入白名单也不会放行；
- 任何会发送 Edge 密钥、用户名密码或客户端证书的目标都必须显式加入上述主机白名单，只有“位于私网”不足以获得凭据；
- 发布前和发布请求中都执行真实值校验。

配置应用应在工艺允许的安全边界进行。离散设备优先在两次过程执行之间切换，失败时继续运行旧版本。

## 健康与就绪

| 服务 | 检查 | 含义 |
|---|---|---|
| Platform | `/health` | 中心进程和配置依赖状态 |
| Platform Worker | `:8002/health` 与内网 `:8002/metrics` | Worker 调度心跳持续更新；Prometheus 对不可达和心跳超时分别告警 |
| Optimizer | `/health` | HTTP 进程存活 |
| Optimizer | `/ready` | PyTorch、GPyTorch 和 BoTorch 数值运行时可用 |
| ConnectorHost | `/health` | 现场进程和配置依赖状态 |
| Web | `/health` | 前端服务状态 |
| PostgreSQL | `pg_isready` | 数据库接受连接 |

Platform 不依赖 Optimizer 才能启动。Optimizer 故障期间继续采集、运行记录和检验，只暂停新数值建议。Agent 或模型服务故障也不能阻止核心业务记录。

## 可观测性

生产运行至少监控：

- Edge 最后心跳、配置期望/应用版本和错误；
- 每个设备连接状态、采样时间和积压；
- 事件重复、乱序、最大间隙和补传延迟；
- 运行完整、实际参数、上下文和检验关联覆盖；
- PostgreSQL 连接、磁盘、迁移和慢事务；
- Optimizer 就绪、请求失败和计算时间；
- Agent 工具失败、模型不可用和授权拒绝。

报警应指向可操作对象，例如具体 Edge、设备、配置版本或运行，而不是只显示“系统异常”。

仓库提供一个可选的最小监控 profile，包括 Prometheus、Alertmanager、PostgreSQL exporter 和预置 Grafana 看板：

```bash
docker compose -f docker-compose.app.yml \
  --profile connector-host --profile monitoring up -d --build
```

Grafana、Prometheus 和 Alertmanager 分别只绑定本机 `3001`、`9090` 和 `9093` 端口。启用前必须：

- 修改 `deploy/observability/edge-targets.yml`，为每个 Edge 填入真实目标、`SiteId` 和 `EdgeId`；
- 设置唯一的 `INGOT_GRAFANA_ADMIN_PASSWORD`；
- 用现场拥有的 `INGOT_ALERTMANAGER_CONFIG_PATH` 替换默认配置，并接入经过实测的通知渠道；
- 按容量和数据分级确定 `INGOT_PROMETHEUS_RETENTION`，同时监控 Prometheus 自身磁盘。

仓库中的默认 Alertmanager receiver 刻意不向外发送通知，不能作为“报警已经接通”的证据。这个 profile 消除了“只有指标端点、没有采集和看板”的空档，但仍是单机参考拓扑；它不会把 Compose 变成高可用系统。

## 数据与备份

生成一次应用一致备份时，脚本会短暂停止 Platform API 和 Worker，逻辑导出 PostgreSQL，归档检验附件与工艺知识卷，生成 SHA-256 清单，然后恢复原先运行的写入服务：

```bash
./scripts/backup-app.sh
./scripts/check-backup.sh deploy/backups 24
```

恢复会替换当前 PostgreSQL 数据库和四个文件卷，必须显式传入确认参数。恢复失败时写入服务保持停止，避免在半恢复状态继续产生记录：

```bash
./scripts/restore-app.sh --confirm-replace-all-data deploy/backups/app-YYYYMMDDTHHMMSSZ
```

备份格式使用 `pg_dump --format=custom`，适合逻辑恢复和迁移验证，但不是 PITR。需要更小 RPO 的现场还必须由部署方配置 PostgreSQL 基础备份、持续 WAL 归档、异机保留和定期时间点恢复演练。备份目录包含业务和附件数据，权限不得低于生产系统本身。

至少备份：

- PostgreSQL 数据卷；
- 检验附件；
- 工艺知识文件；
- Edge 本地事件数据库，直到确认全部上送；
- Edge 最后成功采集配置缓存；
- 生产所需的证书、密钥引用和恢复说明。

恢复演练不仅检查服务启动，还要验证：

- 运行、上下文和检验关联仍然存在；
- 实验、证据和审核记录可读取；
- Edge 离线积压能够无重复补传；
- 历史观察能够按原版本重建；
- 已知项目能够重新生成同一分析输入哈希。

## 升级

1. 阅读 `CHANGELOG.md` 并识别数据模型或配置变化；
2. 备份数据库、附件和 Edge 配置缓存；
3. 在测试环境执行迁移和回放；
4. 运行 `scripts/verify.sh`；
5. 升级 Platform 和数据库依赖；
6. 分批升级 Edge，确认旧配置持续工作；
7. 检查积压恢复、重复事件和配置收敛；
8. 对一个已知项目执行运行装配、比较和建议回归。

## 安全最小集

- 不向非必要网段暴露 PostgreSQL、Optimizer 或 ConnectorHost；
- 更换所有示例密码、令牌和证书；
- 每个 Edge 使用独立身份和最小权限令牌；
- 限制设备网络到所需地址和协议；
- 附件、知识文件和备份目录执行访问控制；
- 用户按岗位分权，质量录入与复核使用不同责任人；
- 工程师审核实验后才允许进入现场执行；
- 设备硬联锁和现场安全规则独立于模型建议存在；
- 安全事件按 `SECURITY.md` 私下报告。

## 生产验收

上线前至少完成一次：Platform 中断、Edge 重启、网络断开、错误配置发布、数据库恢复、Optimizer 不可用和模型服务不可用演练，并证明采集和正式业务记录按设计降级或恢复。

先使用只读业务闭环验收脚本核对当前部署是否已经具备版本化配置、运行中的真实来源、完整工装、生产上下文、运行—检验关联、数据准入、候选边界、实验结果和岗位分权：

```bash
export INGOT_PLATFORM_URL=https://ingot.example.com
export INGOT_ACCEPTANCE_USERNAME=acceptance-admin
export INGOT_ACCEPTANCE_PASSWORD='由现场密钥管理提供'
node scripts/verify-pilot-workflow.mjs \
  --output artifacts/pilot-workflow.json
```

脚本只登录并读取业务 API，不会创建、发布或修改生产记录。输出 `business-workflow-passed` 只表示业务闭环具备可核验数据，不等于生产准入；以下备份恢复、故障、容量、监控告警和连续观察证据仍然必须独立完成。

`.env.example` 中的 RPO、RTO、离线窗口、积压时限、峰值负载和连续观察周期是部署声明。声明本身不是验收证据。完成现场演练后，加载这些目标，并补充实测值和稳定证据标识：

```bash
set -a; . ./.env; set +a
export INGOT_MEASURED_RPO_MINUTES=10
export INGOT_MEASURED_RTO_MINUTES=45
export INGOT_MEASURED_EDGE_OFFLINE_HOURS=24
export INGOT_MEASURED_BACKLOG_AGE_SECONDS=600
export INGOT_MEASURED_CAPACITY_EVENT_RATE_PER_SECOND=2000
export INGOT_MEASURED_CAPACITY_SAMPLE_POINTS_PER_SECOND=30000
export INGOT_OBSERVED_CONTINUOUS_HOURS=168
export INGOT_BACKUP_EVIDENCE=backup-20260820-001
export INGOT_PITR_DRILL_ID=pitr-20260820-001
export INGOT_FAILURE_DRILL_ID=failure-20260820-001
export INGOT_DATABASE_HA_EVIDENCE=database-ha-20260820-001
export INGOT_FILE_RECOVERY_EVIDENCE=file-recovery-20260820-001
export INGOT_EDGE_REPLAY_EVIDENCE=edge-replay-20260820-001
export INGOT_DETERMINISM_EVIDENCE=determinism-20260820-001
export INGOT_SITE_ISOLATION_EVIDENCE=site-isolation-20260820-001
export INGOT_RUNBOOK_EVIDENCE=runbook-review-20260820-001
export INGOT_MONITORING_EVIDENCE=grafana-snapshot-20260820-001
export INGOT_ALERT_ROUTING_EVIDENCE=alert-route-20260820-001
export INGOT_ACCEPTANCE_REVIEWER=quality-owner
./scripts/verify-production-acceptance.sh artifacts/production-acceptance.txt
```

脚本会校验门槛、拒绝覆盖已有工件，并为结果生成 SHA-256；失败结果同样会保留。它只固化声明、实测值和证据引用，不会替代证据真实性复核，也不会自行执行 PITR 或容量测试。

隔离 Compose 环境可以自动演练 Optimizer、Worker 和 API 进程中断，脚本会恢复被停止的服务并生成校验和工件：

```bash
INGOT_DRILL_ENVIRONMENT=isolated \
  ./scripts/drill-compose-failures.sh --confirm-isolated-environment \
  artifacts/compose-failure-drill.txt
```

该脚本明确拒绝在未标记的环境运行，也不会停止 PostgreSQL。网络分区、Edge 断电补传、数据库 HA/PITR、错误配置、模型服务故障和恢复后数据完整性仍需按现场拓扑单独演练。任何一次脚本通过都不能单独构成生产准入。
