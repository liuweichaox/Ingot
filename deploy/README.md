# Ubuntu Docker 公网部署

本目录用于部署 Ingot 官网和文档站：

- `ingotstack.com` 与 `www.ingotstack.com`：官网；
- `docs.ingotstack.com`：中英文文档；
- Caddy 自动申请和续期免费的 Let's Encrypt/ZeroSSL 公共证书；
- Caddy 的证书与账户数据保存在固定命名卷中，重建和切换站点不会删除。

## 1. 准备服务器与 DNS

建议 Ubuntu 24.04 LTS。服务器需要公网 IPv4/IPv6，并允许入站 TCP `22`、`80`、`443` 和 UDP `443`。

如果启用了 UFW，可执行：

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp
```

在 DNS 服务商添加：

| 记录 | 类型 | 值 |
| --- | --- | --- |
| `@` | A/AAAA | 服务器公网地址 |
| `www` | A/AAAA 或 CNAME | 服务器地址或 `ingotstack.com` |
| `docs` | A/AAAA 或 CNAME | 服务器地址或 `ingotstack.com` |

如果服务器没有 IPv6，不要添加 AAAA 记录。先等待 DNS 生效，再启动部署，否则证书签发会重试。

## 2. 安装 Docker

```bash
sudo ./deploy/install-docker-ubuntu.sh
```

安装脚本使用 Docker 官方 Ubuntu 软件源。首次安装后重新登录 SSH，让 docker 用户组生效。

## 3. 选择部署内容

```bash
# 只部署官网
./deploy/deploy.sh site

# 只部署文档
./deploy/deploy.sh docs

# 同时部署官网和文档
ACME_EMAIL=admin@ingotstack.com ./deploy/deploy.sh all
```

每个选择都是完整目标状态。例如从 `all` 执行 `site` 会移除文档容器，但不会删除证书卷。`ACME_EMAIL` 可选，建议设置，便于接收证书服务通知。

临时使用 HTTP 部署时：

```bash
HTTP_ONLY=true ./deploy/deploy.sh all
```

该模式不申请证书，也不启用 HTTP→HTTPS 跳转。恢复 HTTPS 时移除 `HTTP_ONLY=true` 后重新部署。

自定义域名时使用：

```bash
SITE_DOMAIN=example.com DOCS_DOMAIN=docs.example.com ./deploy/deploy.sh all
```

## 4. 运维

```bash
./deploy/deploy.sh status
./deploy/deploy.sh logs
./deploy/deploy.sh logs gateway

# 拉取新代码后重新构建当前目标（示例为全部）
git pull --ff-only
./deploy/deploy.sh all

# 停止容器；证书卷仍保留
./deploy/deploy.sh stop

# 备份证书与 Caddy 状态
./deploy/deploy.sh backup
```

不要使用 `docker compose down -v` 或手动删除 `ingot-caddy-data`、`ingot-caddy-config` 卷。官网和文档没有运行期业务数据库，站点内容来自 Git 仓库并在镜像构建时固化。

## 故障排查

- 证书失败：确认 DNS 已指向本机，云防火墙和 UFW 已开放 TCP 80/443，并检查 `./deploy/deploy.sh logs gateway`。
- 页面未更新：确认拉取了最新提交，再执行同一目标的部署命令重新构建镜像。
- 查看实际目标：`cat deploy/runtime/target`。
- Caddy 配置位于 `deploy/runtime/Caddyfile`，由脚本生成，不应手工编辑。
