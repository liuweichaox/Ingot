# 部署与运行

## 推荐拓扑

```text
现场数据源                  工厂服务器
控制系统 / 仪器 / 视觉 / 检验 / 业务系统
          └─ Edge 或数据适配器 ───→ Platform API
                              ├─ PostgreSQL / TimescaleDB
                              ├─ Optimizer
                              └─ Platform Web
```

Edge 靠近设备部署。Platform、数据库、优化器和 Web 可在同一台工厂服务器使用 Compose 部署，也可按相同网络契约拆分。

## 配置

复制示例：

```bash
cp .env.example .env
```

必须修改：

- `INGOT_POSTGRES_PASSWORD`
- `INGOT_EDGE_TOKEN`
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

## 健康检查

| 服务 | 检查 |
|---|---|
| Platform | `/health` |
| Optimizer | `/ready` |
| Connector | `/health` |
| Web | `/health` |
| PostgreSQL | `pg_isready` |

`/health` 只表示优化 HTTP 进程存活；`/ready` 会验证 PyTorch、GPyTorch 和 BoTorch 数值运行时。

Platform 不依赖 Optimizer 才能启动。优化器故障期间仍可采集和检验，但不能生成新建议。

## 数据与备份

至少备份：

- PostgreSQL 数据卷；
- 检验附件；
- 工艺知识文件；
- Edge 本地事件数据库，直到确认全部上送。

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
