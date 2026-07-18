#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if rg -n -i '/api/v1/agent|connector-workspaces|approve-package|AgentView|桌面 Agent|代码生成' \
  src/Ingot.Central.Web/src; then
  echo "Central Web must remain Chat-only." >&2
  exit 1
fi

if rg -n -i --glob '!**/dist/**' --glob '!**/node_modules/**' --glob '!**/tests/**' \
  '/api/v1/agent|connector-workspaces|approve-package|ConnectorBuilder|PackagingApprovers|Ingot Agent Desktop|desktop Agent' \
  src tests docker-compose.app.yml .github; then
  echo "Desktop code-generation surfaces are forbidden." >&2
  exit 1
fi

if rg -n --glob '!**/dist/**' --glob '!**/node_modules/**' \
  'Ingot\.Edge\.Agent|AgentDataAccess|EnableMultiAgent|MultiAgentEnabled|multiAgentEnabled|deepAnalysisEnabled' \
  src tests README.md README.en.md docs site/app docs-site/app docker-compose.app.yml .github; then
  echo "Legacy product names and compatibility configuration are forbidden." >&2
  exit 1
fi

echo "Central Web product boundaries verified."
