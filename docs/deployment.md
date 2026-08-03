# 部署与运行

## 推荐拓扑

```text
现场数据源                         工厂服务器
控制系统 / 仪器 / 视觉 / 检验 / MES
          └─ Edge ConnectorHost ───→ Platform API（模块化单体）
                                      ├─ PostgreSQL / TimescaleDB
                                      ├─ 附件与工艺知识文件
                                      ├─ 内嵌 Agent / Chat / 数据工具
                                      ├─ Optimizer（独立无状态服务）
                                      └─ Platform Web（独立 React 前端）
```

Edge ConnectorHost 始终作为独立实例运行，拥有独立的 `EdgeId`、进程或容器、数据卷和启停生命周期。小型现场可把 ConnectorHost 与 Platform 放在同一台物理服务器，但仍分别运行和恢复；较大现场把 ConnectorHost 部署到设备附近。Platform API、数据库、Optimizer 和 Platform Web 可在同一台工厂服务器使用 Compose 部署，也可按相同网络契约拆分。`Edge.Application`、`Edge.Infrastructure`、`Platform.Infrastructure` 和 Agent 是代码层类库，不是独立 Compose 服务。

### Edge 划分

- 同一 OT 网段或安全区内、可直接稳定访问且允许共同中断的多台设备，由一个 Edge 执行多份独立采集配置；
- 跨 VLAN、安全区或物理隔离网络的设备，分别部署能够直接访问对应设备的 Edge；
- 不能接受与其他设备同时停采的关键设备，以及事件率、CPU、内存或本地积压显著较高的设备，使用独立 Edge；
- 划分时按共享主机、电源、交换机、网络链路和维护窗口判断共同故障范围，并以可接受的停采范围和恢复时间确定实例数量。

Website 和 Docs 不属于工厂运行时，使用 `deploy/compose.yml` 单独部署到公开站点拓扑。

## 配置

复制示例：

```bash
cp .env.example .env
```

必须修改：

- `INGOT_POSTGRES_PASSWORD`
- `INGOT_EDGE_ID`（安装后保持不变，每台 Edge 唯一）
- `INGOT_EDGE_TOKEN`
- `INGOT_CONNECTOR_TOKEN`
- `INGOT_CONNECTOR_LOCAL_TOKEN`
- `INGOT_ADMIN_PASSWORD`

仓库中的 `.env` 被忽略，不能提交真实凭据。

### 内网模型服务

启用 Chat 时，模型服务必须提供 OpenAI-compatible `/v1` API。设置
`INGOT_CHAT_BASE_URL`、`INGOT_CHAT_FAST_MODEL`、`INGOT_CHAT_REASONING_MODEL` 和
`OPENAI_API_KEY`；令牌可以是本地服务签发的内部令牌。Platform 启动时调用 `/v1/models`，
只有快速和推理两个模型标识都存在才进入服务。模型只接收已经验证并留存内容哈希的只读工具结果。

## 启动

```bash
docker compose -f docker-compose.app.yml up -d --build
```

现场连接器使用 `connector-host` profile：

```bash
docker compose -f docker-compose.app.yml --profile connector-host up -d --build
```

同机部署只合并物理服务器，不合并服务实例。ConnectorHost 与 Platform 使用各自的数据卷、健康检查和重启策略；停止或升级其中一个时，另一个仍可独立运行。

连接真实数据源前，应先配置地址或接口、采集令牌和数据映射，不要直接使用示例值。

采集配置由 Platform 按 `EdgeId` 发布，Edge 主动拉取并把最后一次成功版本保存到
`Data/acquisition-deployments.json`。Platform 暂时不可用或 Edge 重启时继续运行该缓存版本；
正式环境禁止静默切换到未版本化的本地 fallback。Platform 明确返回零配置时，Edge 停止采集并在
采集状态中报告错误。

数据源配置在发布前必须通过一次真实设备验证。Platform 把待验证配置代理到设备所在 Edge：
HTTP/MQTT 读取一份 JSON 样本并返回字段树；OPC UA 浏览并读取变量节点；Modbus TCP 和
MELSEC 只读取用户明确填写的寄存器，不执行可能影响 PLC 的地址盲扫。页面展示原始值、按
`换算值 = 原始值 × 倍率 + 偏移` 得到的值、类型和平台单位。发布请求会在服务端再次执行验证；
连接失败、必需点位缺失或换算失败时不会发布。

## 健康检查

| 服务 | 检查 |
|---|---|
| Platform | `/health` |
| Optimizer | `/ready` |
| Connector | `/health` |
| Web | `/health` |
| PostgreSQL | `pg_isready` |

Optimizer 的 `/health` 只表示其 HTTP 进程存活；Optimizer 的 `/ready` 会验证 PyTorch、GPyTorch 和 BoTorch 数值运行时。Platform、Connector 和 Web 的 `/health` 分别检查各自进程及其配置的依赖。

Platform 不依赖 Optimizer 才能启动。优化器故障期间仍可采集和检验，但不能生成新建议。

## 数据与备份

至少备份：

- PostgreSQL 数据卷；
- 检验附件；
- 工艺知识文件；
- Edge 本地事件数据库，直到确认全部上送。
- Edge 的 `acquisition-deployments.json` 最后成功配置缓存。

恢复演练必须检查实验、周期、检验和证据关联，而不只检查数据库能启动。

## 升级

1. 备份数据库和附件；
2. 阅读 `CHANGELOG.md`；
3. 在测试环境运行迁移；
4. 执行 `scripts/verify.sh`；
5. 滚动更新中心服务；
6. 确认 Edge 积压恢复；
7. 对一个已知项目执行观察装配和建议回归。

## 工厂内部部署的安全最小集

能力优先不等于忽略最基本边界。即使在内部网络，也至少应：

- 不暴露 PostgreSQL、Optimizer 和 Connector 到非必要网段；
- 更换示例密码和令牌；
- 使用独立 Edge 上送令牌；
- 备份并限制附件目录；
- 由工程师审核实验后再执行。
