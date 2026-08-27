#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if grep -rEni --exclude-dir=dist --exclude-dir=node_modules \
  '/api/v1/agent|connector-workspaces|approve-package|AgentView|桌面 Agent|代码生成' \
  apps/platform/src; then
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

# Production imports and site-specific mappings are deployment-controlled
# evidence, not repository assets. Deleted tracked files are ignored here so a
# cleanup commit can run the gate before it is created; CI checkouts contain any
# tracked file and will reject it.
sensitive_paths=()
while IFS= read -r path; do
  [[ -e "$path" ]] || continue
  case "$path" in
    tests/fixtures/synthetic/*|tools/*/examples/synthetic/*)
      ;;
    .ingot-import/*|mapping-*.json|*.csv|*.parquet|*.xlsx|*.xls|*.db)
      sensitive_paths+=("$path")
      ;;
  esac
done < <(git ls-files)

if (( ${#sensitive_paths[@]} > 0 )); then
  printf '%s\n' "${sensitive_paths[@]}"
  echo "Production data or site-specific import mappings must not be tracked. Synthetic data files are allowed only under tests/fixtures/synthetic or tools/*/examples/synthetic." >&2
  exit 1
fi

if grep -RInE --exclude='package-lock.json' --exclude='verify-product-scope.sh' \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=.next \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=.venv \
  'IMPORT-REAL-DATA|measured_thickness_raw|vacuum_degree_kpa' \
  README.md README.en.md docs apps src tests tools scripts; then
  echo "Repository content contains site-specific production import markers." >&2
  exit 1
fi

echo "Platform Web product boundaries verified."
