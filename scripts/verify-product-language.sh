#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

public_files=(
  README.md README.en.md CONTRIBUTING.md CONTRIBUTING.en.md SECURITY.md
  docs apps/website/app apps/docs-site/app
  src/platform/Ingot.Platform.Web/src
)

if grep -RInE --exclude='package-lock.json' \
  '证据|事实|溯源|语义|候选解释|反证|假设|主张|依据|产品面|深度调查|深度分析' "${public_files[@]}"; then
  echo "Public copy contains internal terminology. Use factory-language terms from docs/product-language.md." >&2
  exit 1
fi

if grep -RIniE --exclude='package-lock.json' \
  '\<(evidence|facts?|provenance|semantics?|artifacts?|actors?|hypotheses?|claims?)\>|deep[- ]investigation|verified records' "${public_files[@]}"; then
  echo "Public copy contains internal English terminology. Use docs/product-language.en.md." >&2
  exit 1
fi
