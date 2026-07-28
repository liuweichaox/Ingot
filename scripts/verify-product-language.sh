#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

public_files=(
  README.md README.en.md CONTRIBUTING.md CONTRIBUTING.en.md SECURITY.md
  docs apps/website/app apps/docs-site/app
  src/platform/Ingot.Platform.Web/src
)

# Guard against obsolete product narratives. Evidence, hypotheses, provenance,
# and uncertainty are intentional terms in the current AI process R&D product.
if grep -RInE --exclude='package-lock.json' \
  '制造生产数据与工艺分析系统|生产事件平台|工艺改进工作台|候选解释|产品面|深度调查' "${public_files[@]}"; then
  echo "Public copy contains obsolete product terminology. Follow docs/design.md and docs/brand.md." >&2
  exit 1
fi

if grep -RIniE --exclude='package-lock.json' \
  'manufacturing production data and process analysis system|production event platform|process improvement workspace|candidate explanation|deep investigation' "${public_files[@]}"; then
  echo "Public copy contains obsolete English product terminology. Follow docs/design.en.md and docs/brand.en.md." >&2
  exit 1
fi
