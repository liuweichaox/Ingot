#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

bash scripts/verify-architecture.sh
bash scripts/verify-product-scope.sh
bash scripts/verify-product-language.sh

for required_file in \
  src/platform/Ingot.Platform.Api/Dockerfile \
  src/platform/Ingot.Platform.Web/Dockerfile \
  src/edge/Ingot.Edge.ConnectorHost/Dockerfile \
  deploy/docker/site.Dockerfile \
  deploy/docker/docs.Dockerfile; do
  test -f "$required_file"
done

dotnet build Ingot.sln --disable-build-servers -m:1
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj --no-build --disable-build-servers -m:1
npm --prefix src/platform/Ingot.Platform.Web ci
npm --prefix src/platform/Ingot.Platform.Web run build
npm --prefix src/platform/Ingot.Platform.Web run test
npm --prefix src/platform/Ingot.Platform.Web run lint
npm --prefix src/platform/Ingot.Platform.Web audit --omit=dev

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
  INGOT_EDGE_TOKEN=verification-edge-token-0001 \
  INGOT_OPERATOR_TOKEN=verification-operator-token-0001 \
  INGOT_CONNECTOR_TOKEN=verification-connector-token-0001 \
    docker compose -f "$compose_file" config --quiet
done
docker compose -f deploy/compose.yml --profile site --profile docs config --quiet
git diff --check
