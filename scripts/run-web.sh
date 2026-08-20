#!/usr/bin/env bash
# 一键把 Ingot 平台（含 Web）跑起来。
# 在有 Docker（Docker Desktop + WSL 集成）的机器上跑：
#   cd <仓库根>；bash scripts/run-web.sh
#
# 关键点：密钥写进仓库根的 .env（docker compose 自动读取），只生成一次。
# 这样重启不会换 Postgres 口令而废掉数据卷。管理员账号固定 admin，口令写在 .env 里。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
ENV_FILE="$ROOT/.env"
COMPOSE="docker compose -f docker-compose.app.yml"

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: 找不到 docker。请安装 Docker Desktop 并在 Settings → Resources → WSL Integration 里勾选你的发行版。"
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  echo "ERROR: docker 守护进程没跑。打开 Docker Desktop 等它就绪后重试。"
  exit 1
fi

gen() { openssl rand -hex 24; }

if [[ ! -f "$ENV_FILE" ]]; then
  echo ">> 首次运行：生成密钥写入 .env（此文件含机密，勿提交，已在 .gitignore）"
  cat > "$ENV_FILE" <<EOF
# Ingot 本地运行机密 —— 自动生成，勿提交
INGOT_POSTGRES_PASSWORD=$(gen)
INGOT_SITE_ID=SITE-LOCAL-001
INGOT_EDGE_ID=EDGE-LOCAL-001
INGOT_EDGE_TOKEN=$(gen)
INGOT_OPERATOR_TOKEN=$(gen)
INGOT_CONNECTOR_TOKEN=$(gen)
INGOT_CONNECTOR_LOCAL_TOKEN=$(gen)
INGOT_ADMIN_USERNAME=admin
INGOT_ADMIN_PASSWORD=$(gen)
INGOT_AUTH_MODE=Disabled
INGOT_ALLOW_INSECURE_DEMO=true
INGOT_AUTH_REQUIRE_HTTPS=false
EOF
else
  echo ">> 复用已存在的 .env"
fi

for required_key in \
  INGOT_POSTGRES_PASSWORD INGOT_SITE_ID INGOT_EDGE_ID INGOT_EDGE_TOKEN \
  INGOT_CONNECTOR_TOKEN INGOT_CONNECTOR_LOCAL_TOKEN; do
  if ! grep -qE "^${required_key}=.+" "$ENV_FILE"; then
    echo "ERROR: .env 缺少必填配置 ${required_key}；请按 .env.example 补齐后重试。" >&2
    exit 1
  fi
done

# 确保 .env 不会被提交
if [[ -f "$ROOT/.gitignore" ]] && ! grep -qxF '.env' "$ROOT/.gitignore"; then
  echo '.env' >> "$ROOT/.gitignore"
  echo 'build-output.log' >> "$ROOT/.gitignore"
fi

echo; echo "===== 构建 + 启动（platform-api / platform-web / postgres / connector-host） ====="
$COMPOSE --profile connector-host up -d --build

echo; echo "===== 等待 platform-api 健康 ====="
for i in $(seq 1 60); do
  status="$($COMPOSE ps --format '{{.Name}} {{.Health}}' 2>/dev/null | grep platform-api || true)"
  echo "  [$i] $status"
  if echo "$status" | grep -q healthy; then break; fi
  sleep 3
done

echo; echo "===== 平台已启动（原型模式） ====="
echo "  Web:      http://localhost:3000"
echo "  API:      http://localhost:8000/health"
echo "  认证:     Disabled（固定 operator 身份）"
echo
echo ">> 打开 http://localhost:3000 直接进入。停止：$COMPOSE down（加 -v 连数据卷一起删）"
