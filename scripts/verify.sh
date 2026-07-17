#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet build Ingot.sln
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj --no-build
dotnet run --project src/Ingot.Edge.Agent --no-build -- \
  --validate-configs --config-dir examples/device-configs
dotnet run --project src/Ingot.Edge.Agent --no-build -- \
  --validate-configs --config-dir examples/scenarios

npm --prefix src/Ingot.Central.Web ci
npm --prefix src/Ingot.Central.Web run build
npm --prefix src/Ingot.Central.Web audit --omit=dev

for script in scripts/*.sh; do
  bash -n "$script"
done
python3 - <<'PY'
import ast
from pathlib import Path

ast.parse(Path("tools/webhook_receiver.py").read_text(encoding="utf-8"))
PY

docker compose -f docker-compose.events.yml config --quiet
git diff --check
