# 快速开始

> 文档状态：**当前操作指南**。本页提供本地部署、启动与登录说明。能力范围见[当前状态](status.md)。

## 选择路径

| 目标 | 使用路径 | 完成标志 |
|---|---|---|
| 部署系统 | [启动完整栈](#启动完整栈) | Web、API、Worker、Optimizer 和数据库健康，迁移成功退出；无需外部系统账号或连接 |
| 验证当前生产证据流程 | [配方优化试点指南](pilot.md) | 有效生产运行证据和第一份下一配方建议 |
| 准备生产环境 | [生产架构](production-architecture.md) → [部署运维](deployment.md) | 站点独立完成安全、恢复、容量和观察验收 |
| 参与开发 | [贡献指南](https://github.com/liuweichaox/Ingot/blob/main/CONTRIBUTING.md) | 本地通过 `./scripts/verify.sh` |

当前能力和验证成熟度统一见[当前状态](status.md)。

## 启动完整栈

需要 Git、Docker Engine 或 Docker Desktop，以及 Docker Compose v2。Compose 路径不要求主机预装 .NET、Node.js、Python 或 uv。

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
```

修改 `.env` 中的数据库密码和管理员配置。至少替换所有 `change-this-` 占位值；生产环境必须使用随机生成且彼此不同的密码和令牌。

参考 Compose 即使未启用可选连接器，也要求填写 `INGOT_SITE_ID`、`INGOT_EDGE_ID`、`INGOT_EDGE_TOKEN` 和 `INGOT_CONNECTOR_LOCAL_TOKEN`，用于 Platform 绑定。初次评估可保留示例本地 ID；两个令牌须替换为至少 24 个字符、彼此不同的随机密钥。仅启用 `connector-host` 时才配置 `INGOT_CONNECTOR_TOKEN`。这些配置不会自动连接设备。

Windows PowerShell 可用 `Copy-Item .env.example .env` 替代 `cp`。`.env` 保留在本机，不提交到版本库。

先校验配置，再启动：

```bash
docker compose -f docker-compose.app.yml config --quiet
docker compose -f docker-compose.app.yml up -d --build
```

首次构建会下载构建与运行镜像、TimescaleDB 镜像，以及包含 PyTorch 的 Python 数值依赖。命令结束后检查全部容器状态：

```bash
docker compose -f docker-compose.app.yml ps -a
```

至少确认：

- `platform-migrate` 成功退出；
- `postgres`、`optimizer`、`platform-api` 和 `platform-web` 为 `healthy`；
- `platform-worker` 持续为 `healthy`；
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

此部署启动 Ingot 自身的 Web、API、数据库、优化器和后台服务，无需外部业务系统账号或连接。设备及企业系统连接器按需配置和启用。登录后从工艺配置管理工艺变量和配方版本；每次工艺运行就是一次实验，运行和质量记录共同保存实验事实，具体能力与推荐所需数据见[当前状态](status.md)。

## 常见启动问题

页面无法访问时先检查状态和最近日志：

```bash
docker compose -f docker-compose.app.yml ps -a
docker compose -f docker-compose.app.yml logs --tail=200
```

若出现 `unexpected EOF`、`short read` 或拉取超时，通常是镜像下载中断；重新执行 `up -d --build` 会复用已完成层。不要为了排障直接删除数据卷。更多诊断见[部署运维](deployment.md#启动与停止)。

## 验证首次使用

1. 登录后打开工艺配置，确认能够访问工艺变量和配方版本。
2. 检查 `http://localhost:8002/health` 的 Worker 状态，保留迁移结果与服务状态以便排障。
3. 按试点指南使用自己有权处理的数据。部署健康不会自动生成生产运行、检验结果或符合准入条件的优化观察。

参考 Compose 的应用端口均绑定 `127.0.0.1`。远程用户需配置带 TLS 和认证的入口；仅修改访问 URL 不会公开服务。

## 下一步

- 要接入一组真实或代表性配方运行：继续[配方优化试点指南](pilot.md)；
- 要了解身份、点位和映射：阅读[数据接入](data-connection.md)；
- 要判断哪些能力已经验证：阅读[当前状态](status.md)；
- 要长期部署：按[生产架构](production-architecture.md)选择基础研发、现场接入或高可用等级，完成相应验收。
