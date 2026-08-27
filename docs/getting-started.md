# 快速开始

> 文档状态：**当前操作指南**。本页提供合成数据流程体验和本地完整栈启动说明。真实试点要求见[配方优化试点指南](pilot.md)。

## 选择路径

| 目标 | 使用路径 | 完成标志 |
|---|---|---|
| 评估产品流程 | [五分钟合成演示](#五分钟合成演示) | 完成超差运行比较并进入配方优化流程 |
| 本地运行完整系统 | [启动完整栈](#启动完整栈) | Web、API、Optimizer 和数据库健康 |
| 准备真实项目 | [配方优化试点指南](pilot.md) | 第一批可信优化观察和第一份下一配方建议 |
| 准备生产环境 | [生产架构](production-architecture.md) → [部署运维](deployment.md) | 站点独立完成安全、恢复、容量和观察验收 |
| 参与开发 | [贡献指南](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.md) | 本地通过 `./scripts/verify.sh` |

当前能力和验证成熟度统一见[当前状态](status.md)。

## 五分钟合成演示

该路径要求 Node.js 22.22+；无需数据库、设备或 Docker。

首次运行安装前端依赖：

```bash
npm --prefix apps/platform ci
```

在两个终端分别启动合成业务 API 和前端：

```bash
# 终端 1
node scripts/platform-demo.mjs
```

```bash
# 终端 2
npm --prefix apps/platform run demo
```

打开 `http://127.0.0.1:3001`：

- `demo / demo`：查看工程师工作流；
- `admin / admin12345`：查看系统管理和试点验收入口。

工作台会引导用户打开一条超差运行、核对已复核检验、选择合格基线、比较实际条件与轨迹，并查看候选原因和配方优化入口。配方优化工作区会展示真实运行如何形成观察以及下一配方建议如何保持人工确认。所有数据均为合成数据；该演示验证页面和流程，不证明真实工艺收益。

结束时在两个终端按 `Ctrl+C`。

## 启动完整栈

需要 Git、Docker Engine 或 Docker Desktop，以及 Docker Compose v2。Compose 路径不要求主机预装 .NET、Node.js、Python 或 uv。

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

修改 `.env` 中的数据库密码、Edge 上送令牌和管理员配置。至少替换所有 `change-this-` 占位值；生产环境必须使用随机生成且彼此不同的密码和令牌。

先校验配置，再启动：

```bash
docker compose -f docker-compose.app.yml config --quiet
docker compose -f docker-compose.app.yml up -d --build
```

首次构建会下载 .NET、Node、Python、PyTorch 和 TimescaleDB 镜像。命令结束后检查全部容器状态：

```bash
docker compose -f docker-compose.app.yml ps -a
```

至少确认：

- `platform-migrate` 成功退出；
- `postgres`、`optimizer`、`platform-api` 和 `platform-web` 为 `healthy`；
- `platform-worker` 和 `connector-host` 持续为 `healthy`；
- 没有容器处于反复重启状态。

然后访问：

```text
http://localhost:3000       工程工作台
http://localhost:8000/health
http://localhost:8000/openapi/v1.json
http://localhost:8100/ready
```

使用 `.env` 中的 `INGOT_ADMIN_USERNAME` 和 `INGOT_ADMIN_PASSWORD` 登录。若管理员密码留空，Migrator 只在用户表为空时生成随机口令：

```bash
docker compose -f docker-compose.app.yml logs platform-migrate
```

后续修改 `.env` 不会重置已有账户。

## 常见启动问题

页面无法访问时先检查状态和最近日志：

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
```

若出现 `unexpected EOF`、`short read` 或拉取超时，通常是镜像下载中断；重新执行 `up -d --build` 会复用已完成层。不要为了排障直接删除数据卷。更多诊断见[部署运维](deployment.md#启动与停止)。

## 下一步

- 要接入一组真实或代表性配方运行：继续[配方优化试点指南](pilot.md)；
- 要了解身份、点位和映射：阅读[数据接入](data-connection.md)；
- 要判断哪些能力已经验证：阅读[当前状态](status.md)；
- 要部署生产环境：先完成[生产架构](production-architecture.md)定义的站点验收。
