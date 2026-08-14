# 部署运维

> 文档状态：**当前运维指南**。部署目标是让数据采集、业务记录和工程决策在厂内长期可靠运行；公开官网和文档站不属于工厂运行时。

## 推荐拓扑

```text
现场数据源                              工厂运行环境
控制系统 / 仪器 / 视觉 / 检验 / MES
          └─ Edge ConnectorHost ───→ Platform API
                                      ├─ PostgreSQL / TimescaleDB
                                      ├─ 附件与工艺知识文件
                                      ├─ Optimizer（独立无状态服务）
                                      ├─ Agent / Chat（Platform 内嵌业务能力）
                                      └─ Platform Web（独立 React 前端）
```

Platform API 是中心业务运行单元。`Edge.Application`、`Edge.Infrastructure`、`Platform.Infrastructure` 和 Agent 是代码层类库，不是独立 Compose 服务。

### Edge 划分原则

每个 Edge ConnectorHost 拥有独立 `EdgeId`、进程或容器、数据卷、配置缓存和生命周期。

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
- `INGOT_EDGE_ID`：安装后保持不变，每台 Edge 唯一
- `INGOT_EDGE_TOKEN`
- `INGOT_CONNECTOR_TOKEN`
- `INGOT_CONNECTOR_LOCAL_TOKEN`
- `INGOT_ADMIN_PASSWORD`

生产环境必须使用 `INGOT_AUTH_MODE=Local` 或 `INGOT_AUTH_MODE=Oidc`。`INGOT_AUTH_MODE=Disabled`
只允许在明确隔离的演示环境使用，并且必须同时设置 `INGOT_ALLOW_INSECURE_DEMO=true`；该模式会把
所有请求映射到固定的开发身份，不能暴露到厂内网络或反向代理之后。

不要提交 `.env` 或真实设备凭据。设备密码和证书应使用现场允许的密钥管理方式注入。

### 本地模型服务

启用 Chat 时，模型服务应提供 OpenAI-compatible `/v1` 接口。配置 `INGOT_CHAT_BASE_URL`、`INGOT_CHAT_FAST_MODEL`、`INGOT_CHAT_REASONING_MODEL` 和 `OPENAI_API_KEY`。Platform 只在配置的模型标识可用时启用相应角色。

模型服务不是采集、检验或数值优化的启动依赖。发送给模型的内容必须经过授权工具和业务权限控制。

## 启动与停止

先校验必填环境变量和 Compose 结构：

```bash
docker compose -f docker-compose.app.yml config --quiet
```

构建并启动四个核心服务：

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

首次构建会下载较大的 SDK、PyTorch 和数据库镜像。必须等 `up` 成功结束且 `ps` 显示四个核心服务均为 `healthy` 后，才算启动完成。

| 现象 | 先检查 | 常见原因与处理 |
|---|---|---|
| `ps -a` 没有任何容器 | 构建命令的最后输出 | 构建仍在进行或已中断；重新执行 `up -d --build` |
| `unexpected EOF` 或 `short read` | 镜像下载层 | 网络中断导致层不完整；直接重试，Docker 会复用完整层 |
| Web 未启动、API 已健康 | `logs platform-web` | 前端构建或 Nginx 配置失败 |
| API 反复重启 | `logs platform-api` 和 `logs postgres` | 数据库密码、迁移、目录权限或生产配置校验失败 |
| Optimizer 不健康 | `logs optimizer` 和 `/ready` | Python 数值依赖尚未安装完成或运行时加载失败 |
| 登录口令未知 | `logs platform-api` | 仅管理员首次播种且密码留空时输出随机口令；已有账户不会因修改 `.env` 自动重置 |
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
- 发布前和发布请求中都执行真实值校验。

配置应用应在工艺允许的安全边界进行。离散设备优先在两次过程执行之间切换，失败时继续运行旧版本。

## 健康与就绪

| 服务 | 检查 | 含义 |
|---|---|---|
| Platform | `/health` | 中心进程和配置依赖状态 |
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

## 数据与备份

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
