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
    tools/public-validation/data/fdm-doe-grid.csv|tools/public-validation/data/crossed-barrel.csv|tools/public-validation/data/airfoil-self-noise.csv|tools/public-validation/data/yacht-hydrodynamics.csv|tools/public-validation/data/energy-efficiency.csv|tools/public-validation/data/synchronous-machine.csv|tools/public-validation/data/lnp3-formulations.csv)
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

python3 - <<'PY'
import hashlib
import json
from pathlib import Path

root = Path("tools/public-validation")
protocols = [
    json.loads((root / name).read_text(encoding="utf-8"))
    for name in (
        "protocol-v2.json",
        "protocol-v3.json",
        "protocol-v4.json",
        "protocol-v6.json",
        "protocol-v7.json",
    )
]
allowed = {
    "data/fdm-doe-grid.csv",
    "data/crossed-barrel.csv",
    "data/airfoil-self-noise.csv",
    "data/yacht-hydrodynamics.csv",
    "data/energy-efficiency.csv",
    "data/synchronous-machine.csv",
    "data/lnp3-formulations.csv",
    "data/oer-plate-3496.csv",
    "data/oer-plate-3851.csv",
    "data/oer-plate-3860.csv",
    "data/oer-plate-4098.csv",
}
declared = {
    source["fixture"]
    for protocol in protocols
    for source in protocol["sources"].values()
}
if declared != allowed:
    raise SystemExit("public validation protocol must declare exactly the approved fixtures")
if not (root / "NOTICE.md").is_file():
    raise SystemExit("public validation fixtures require NOTICE.md attribution")
for protocol in protocols:
    for source in protocol["sources"].values():
        path = root / source["fixture"]
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != source["fixture_sha256"]:
            raise SystemExit(f"public validation checksum mismatch: {path}")
PY

if grep -RInE --exclude='package-lock.json' --exclude='verify-product-scope.sh' \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=.next \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=.venv \
  'IMPORT-REAL-DATA|measured_thickness_raw|vacuum_degree_kpa' \
  README.md README.en.md docs apps src tests tools scripts; then
  echo "Repository content contains site-specific production import markers." >&2
  exit 1
fi

echo "Platform Web product boundaries verified."
