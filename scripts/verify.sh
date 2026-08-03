#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet_artifacts="$(mktemp -d)"
trap 'rm -rf -- "$dotnet_artifacts"' EXIT

bash scripts/verify-architecture.sh
bash scripts/verify-product-scope.sh
bash scripts/verify-product-language.sh

for required_file in \
  src/platform/Ingot.Platform.Api/Dockerfile \
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
npm --prefix apps/platform ci
npm --prefix apps/platform run build
npm --prefix apps/platform run test
npm --prefix apps/platform run lint
npm --prefix apps/platform audit --omit=dev

npm --prefix apps/website ci
npm --prefix apps/website run build
node --test apps/website/tests/rendered-html.test.mjs
npm --prefix apps/website run lint
npm --prefix apps/website audit --omit=dev

npm --prefix apps/docs-site ci
npm --prefix apps/docs-site run build
node --test apps/docs-site/tests/export.test.mjs
npm --prefix apps/docs-site run lint
npm --prefix apps/docs-site audit --omit=dev

uv sync --project optimizer --extra service --group dev --locked
uv run --project optimizer --locked pytest

for script in scripts/*.sh deploy/*.sh; do
  bash -n "$script"
done
python3 - <<'PY'
import ast
from pathlib import Path

ast.parse(Path("tools/webhook_receiver.py").read_text(encoding="utf-8"))
PY

for compose_file in docker-compose.app.yml; do
  INGOT_POSTGRES_PASSWORD=verification-postgres-password \
  INGOT_EDGE_ID=verification-edge-0001 \
  INGOT_EDGE_TOKEN=verification-edge-token-0001 \
  INGOT_OPERATOR_TOKEN=verification-operator-token-0001 \
  INGOT_CONNECTOR_TOKEN=verification-connector-token-0001 \
    docker compose -f "$compose_file" config --quiet
done
docker compose -f deploy/compose.yml --profile site --profile docs config --quiet
git diff --check
