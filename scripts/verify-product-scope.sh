#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if grep -rEni --exclude-dir=dist --exclude-dir=node_modules \
  '/api/v1/agent|connector-workspaces|approve-package|AgentView|桌面 Agent|代码生成' \
  src/platform/Ingot.Platform.Web/src; then
  echo "Platform Web must remain Chat-only." >&2
  exit 1
fi

if grep -rEni --exclude-dir=dist --exclude-dir=node_modules --exclude-dir=tests \
  '/api/v1/agent|connector-workspaces|approve-package|ConnectorBuilder|PackagingApprovers|Ingot Agent Desktop|desktop Agent' \
  src tests docker-compose.app.yml .github; then
  echo "Desktop code-generation surfaces are forbidden." >&2
  exit 1
fi

if grep -rEn --exclude-dir=dist --exclude-dir=node_modules \
  'Ingot\.Edge\.Agent|AgentDataAccess|EnableMultiAgent|MultiAgentEnabled|multiAgentEnabled|deepAnalysisEnabled' \
  src tests README.md README.en.md docs apps/website/app apps/docs-site/app docker-compose.app.yml .github; then
  echo "Legacy product names and compatibility configuration are forbidden." >&2
  exit 1
fi

echo "Platform Web product boundaries verified."
