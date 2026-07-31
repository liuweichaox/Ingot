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

Edge ConnectorHost 靠近设备部署。Platform API、数据库、Optimizer 和 Platform Web 可在同一台工厂服务器使用 Compose 部署，也可按相同网络契约拆分。`Edge.Application`、`Edge.Infrastructure`、`Platform.Infrastructure` 和 Agent 是代码层类库，不是独立 Compose 服务。

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

## 启动

```bash
docker compose -f docker-compose.app.yml up -d --build
```

现场连接器使用 `connector-host` profile：

```bash
docker compose -f docker-compose.app.yml --profile connector-host up -d --build
```

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
