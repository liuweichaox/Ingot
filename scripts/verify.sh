#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# dotnet-install.sh 默认安装到用户目录，但非交互式 WSL/CI shell 不一定包含该路径。
# 仅当当前 dotnet 缺少目标 SDK 时才回退，避免覆盖系统中已经可用的 .NET 10。
if ! dotnet --list-sdks 2>/dev/null | grep -qE '^10\.'; then
  user_dotnet="$HOME/.dotnet/dotnet"
  if [[ -x "$user_dotnet" ]] && "$user_dotnet" --list-sdks | grep -qE '^10\.'; then
    export PATH="$HOME/.dotnet:$PATH"
  fi
fi

if ! dotnet --list-sdks 2>/dev/null | grep -qE '^10\.'; then
  echo "需要 .NET SDK 10 才能执行完整验证。" >&2
  exit 1
fi

# uv 的官方用户级安装目录同样可能不在非交互式 WSL/CI PATH 中。
if ! command -v uv >/dev/null 2>&1 && [[ -x "$HOME/.local/bin/uv" ]]; then
  export PATH="$HOME/.local/bin:$PATH"
fi
if ! command -v uv >/dev/null 2>&1; then
  echo "需要 uv 才能执行 optimizer 与现场模拟器测试。" >&2
  exit 1
fi

verification_temp="$(mktemp -d)"
dotnet_artifacts="$verification_temp/dotnet-artifacts"
frontend_workspace="$verification_temp/frontend-workspace"
mkdir -p "$dotnet_artifacts" "$frontend_workspace"
trap 'rm -rf -- "$verification_temp"' EXIT

# Windows 与 WSL 的 npm 原生可选依赖不同，不能共用挂载工作区里的 node_modules。
# 将前端源码复制到 Linux 临时目录验证，既覆盖当前未提交源码，又不删除开发机正在使用的依赖。
tar \
  --exclude='*/node_modules' \
  --exclude='*/node_modules/*' \
  --exclude='*/.next' \
  --exclude='*/.next/*' \
  --exclude='*/dist' \
  --exclude='*/dist/*' \
  --exclude='*/out' \
  --exclude='*/out/*' \
  -C "$repo_root" -cf - apps docs | tar -C "$frontend_workspace" -xf -

bash scripts/verify-architecture.sh
bash scripts/verify-code-comments.sh
bash scripts/verify-product-scope.sh
bash scripts/verify-product-language.sh

for required_file in \
  src/platform/Ingot.Platform.Api/Dockerfile \
  src/platform/Ingot.Platform.Worker/Dockerfile \
  apps/platform/Dockerfile \
  src/edge/Ingot.Edge.ConnectorHost/Dockerfile \
  optimizer/Dockerfile \
  deploy/docker/site.Dockerfile \
  deploy/docker/docs.Dockerfile; do
  test -f "$required_file"
done

dotnet build Ingot.sln --artifacts-path "$dotnet_artifacts" --disable-build-servers -m:1
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj --no-build \
  --artifacts-path "$dotnet_artifacts" --disable-build-servers -m:1
platform_app="$frontend_workspace/apps/platform"
website_app="$frontend_workspace/apps/website"
docs_app="$frontend_workspace/apps/docs-site"

npm --prefix "$platform_app" ci
npm --prefix "$platform_app" run build
npm --prefix "$platform_app" run test
npm --prefix "$platform_app" run lint
npm --prefix "$platform_app" audit --omit=dev

npm --prefix "$website_app" ci
npm --prefix "$website_app" run build
node --test "$website_app/tests/rendered-html.test.mjs"
npm --prefix "$website_app" run lint
npm --prefix "$website_app" audit --omit=dev

npm --prefix "$docs_app" ci
npm --prefix "$docs_app" run build
node --test "$docs_app/tests/export.test.mjs"
npm --prefix "$docs_app" run lint
npm --prefix "$docs_app" audit --omit=dev

optimizer_environment="$verification_temp/optimizer-venv"
UV_PROJECT_ENVIRONMENT="$optimizer_environment" \
  uv sync --project optimizer --extra service --group dev --locked
UV_PROJECT_ENVIRONMENT="$optimizer_environment" \
  uv run --project optimizer --locked pytest

for script in scripts/*.sh deploy/*.sh; do
  bash -n "$script"
done

scripts/test-production-operations.sh
scripts/verify-observability.sh

for compose_file in docker-compose.app.yml; do
  compose_config="$(
    INGOT_POSTGRES_PASSWORD=verification-postgres-password \
    INGOT_SITE_ID=verification-site-0001 \
    INGOT_EDGE_ID=verification-edge-0001 \
    INGOT_EDGE_TOKEN=verification-edge-token-0001 \
    INGOT_OPERATOR_TOKEN=verification-operator-token-0001 \
    INGOT_CONNECTOR_TOKEN=verification-connector-token-0001 \
    INGOT_CONNECTOR_LOCAL_TOKEN=verification-connector-local-token-0001 \
    INGOT_GRAFANA_ADMIN_PASSWORD=verification-grafana-password-0001 \
      docker compose -f "$compose_file" --profile connector-host --profile monitoring config
  )"
  grep -Fq 'EventIngest__EdgeTokens__verification-edge-0001:' <<<"$compose_config"
  grep -Fq 'EventIngest__EdgeSites__verification-edge-0001: verification-site-0001' <<<"$compose_config"
  grep -Fq 'Edge__SiteId: verification-site-0001' <<<"$compose_config"
  grep -Fq 'Edge__EdgeId: verification-edge-0001' <<<"$compose_config"
  grep -Fq 'prom/prometheus:v3.12.0' <<<"$compose_config"
  grep -Fq 'prom/alertmanager:v0.32.1' <<<"$compose_config"
  grep -Fq 'grafana/grafana:13.1.3' <<<"$compose_config"
done
docker compose -f deploy/compose.yml --profile site --profile docs config --quiet
git diff --check
