#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

for required in \
  desktop/package.json \
  desktop/src-tauri/Cargo.toml \
  desktop/src-tauri/tauri.conf.json; do
  test -f "$required" || {
    echo "Missing Desktop Agent file: $required" >&2
    exit 1
  }
done

if rg -n -i '/api/v1/agent|connector-workspaces|approve-package|AgentView|连接器工程 Agent' \
  src/Ingot.Central.Web/src; then
  echo "Central Web must remain Chat-only." >&2
  exit 1
fi

if rg -n -i '/api/v1/chat|check_data_quality|get_cycle_trace|工艺分析|process analysis' \
  desktop/src desktop/src-tauri; then
  echo "Desktop Agent must remain code-generation-only." >&2
  exit 1
fi

if rg -n -i 'AI 分析 Agent|AI Analysis Agent|多 Agent|multi-Agent|Edge Agent|边缘 Agent' \
  README.md README.en.md docs site/app docs-site/app; then
  echo "Public product terminology does not separate Chat, Desktop Agent, and Connector Host." >&2
  exit 1
fi

if rg -n -i --glob '!**/dist/**' --glob '!**/node_modules/**' \
  'test_http_connector|ConnectorTest' \
  src tests README.md README.en.md docs site/app docs-site/app; then
  echo "Desktop Agent and Connector Builder must not connect to source networks." >&2
  exit 1
fi

if rg -n --glob '!**/dist/**' --glob '!**/node_modules/**' \
  'Ingot\.Edge\.Agent|AgentDataAccess|Agent__EnableMultiAgent' \
  src tests README.md README.en.md docs site/app docs-site/app docker-compose.app.yml .github; then
  echo "Legacy product names and compatibility configuration are forbidden." >&2
  exit 1
fi

echo "Chat and Desktop Agent product boundaries verified."
